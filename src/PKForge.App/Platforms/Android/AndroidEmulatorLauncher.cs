using Android.App;
using Android.Content;
using Android.Content.PM;
using AndroidUri = Android.Net.Uri;
using PKForge.App.ViewModels;
using PKForge.Domain;

namespace PKForge.App;

/// <summary>
/// Hands a linked ROM to its emulator, using each family's documented launch contract:
/// RetroArch takes an explicit RetroActivityFuture component with ROM + LIBRETRO extras
/// (the libretro CLI contract: "Only ROM and LIBRETRO are needed"); melonDS takes its
/// EmulatorActivity with a PATH extra (its frontend contract, from melonDS-android's own
/// README); every other emulator gets an ACTION_VIEW with the ROM's URI and MIME, left
/// to Android's resolver — and when nothing resolves, the emulator's own library opens
/// with a clear status message instead of a silent black screen.
/// </summary>
public sealed class AndroidEmulatorLauncher : IEmulatorLauncher
{
    public bool IsInstalled(string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        var manager = Platform.AppContext?.PackageManager
            ?? throw new InvalidOperationException("PackageManager is unavailable.");
        try
        {
            manager.GetPackageInfo(packageId, 0);
            return true;
        }
        catch (PackageManager.NameNotFoundException)
        {
            return false;
        }
    }

    public async Task<bool> LaunchAsync(RomLink link, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(link);
        var app = EmulatorCatalog.App(link.Emulator);
        var package = app.PackageIds.FirstOrDefault(IsInstalled) ?? app.PrimaryPackage;
        if (!IsInstalled(package))
        {
            Services.AppLog.Info("launch", $"{SaveDescriptions.EmulatorName(link.Emulator)} is not installed ({package}).");
            return false;
        }
        if (app.LaunchStyle == RomLaunchStyle.OpenAppOnly)
        {
            // No documented handoff: the emulator's own library is the launch surface.
            return await OpenAppAsync(link.Emulator, cancellationToken).ConfigureAwait(true);
        }

        // One URI per launch: content ids pass through, plain paths become file://.
        var rom = ResolveUri(link.RomUri);
        if (rom is null)
        {
            Services.AppLog.Info("launch", $"The linked ROM cannot be read: {link.RomUri}");
            return false;
        }

        var activity = Platform.CurrentActivity ?? throw new InvalidOperationException("No foreground Android activity is available.");
        var intent = BuildIntent(app.LaunchStyle, package, rom, link);
        if (intent is null) return await OpenAppAsync(link.Emulator, cancellationToken).ConfigureAwait(true);
        return StartActivity(activity, intent, link);
    }

    public Task<bool> OpenAppAsync(EmulatorKind kind, CancellationToken cancellationToken = default)
    {
        var activity = Platform.CurrentActivity ?? throw new InvalidOperationException("No foreground Android activity is available.");
        var app = EmulatorCatalog.App(kind);
        var manager = activity.PackageManager!;
        var opened = false;
        foreach (var id in app.PackageIds.Where(IsInstalled))
        {
            try
            {
                var launch = manager.GetLaunchIntentForPackage(id);
                if (launch is null) continue;
                launch.AddFlags(ActivityFlags.NewTask);
                activity.StartActivity(launch);
                opened = true;
                break;
            }
            catch (ActivityNotFoundException) { }
            catch (Java.Lang.SecurityException) { }
        }
        if (!opened)
            Services.AppLog.Info("launch", $"Could not open {SaveDescriptions.EmulatorName(kind)}: no launchable activity.");
        return Task.FromResult(opened);
    }

    /// <summary>content:// ids pass through; plain file paths become file:// URIs that
    /// exist on disk; anything unreadable returns null (the launch says so).</summary>
    private static AndroidUri? ResolveUri(string romUri)
    {
        if (romUri.StartsWith("content://", StringComparison.Ordinal))
            return AndroidUri.Parse(romUri);
        try
        {
            if (!File.Exists(romUri)) return null;
            return AndroidUri.FromFile(new Java.IO.File(romUri));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The intent family per launch style; each names its own contract.</summary>
    private static Intent? BuildIntent(RomLaunchStyle style, string package, AndroidUri rom, RomLink link) => style switch
    {
        RomLaunchStyle.RetroArch => RetroArchIntent(package, rom, link),
        RomLaunchStyle.PathExtra => PathExtraIntent(package, rom, link),
        RomLaunchStyle.UriData => ViewIntent(rom),
        _ => null,
    };

    /// <summary>
    /// RetroArch's documented CLI contract, verbatim: an explicit component on
    /// RetroActivityFuture (the activity name is com.retroarch.browser.retroactivity.
    /// RetroActivityFuture in every build; only the package differs) with the ROM path
    /// and the core path as string extras. "Only ROM and LIBRETRO are needed" per the
    /// libretro CLI guide; CONFIGFILE points at the standard config so settings stick.
    /// The core path names the installed package's own core directory — RetroArch reads
    /// it itself (our app cannot), and pairs the ROM on its own when the core is absent.
    /// </summary>
    private static Intent RetroArchIntent(string package, AndroidUri rom, RomLink link)
    {
        var intent = new Intent();
        intent.SetComponent(new ComponentName(package, "com.retroarch.browser.retroactivity.RetroActivityFuture"));
        intent.SetAction(Intent.ActionMain);
        intent.AddCategory(Intent.CategoryLauncher);
        intent.AddFlags(ActivityFlags.NewTask);
        intent.PutExtra("ROM", RomPathFor(link));
        var core = CorePathHint(link.Generation, package);
        if (core is not null) intent.PutExtra("LIBRETRO", core);
        var config = ConfigFileFor(package);
        if (config is not null) intent.PutExtra("CONFIGFILE", config);
        return intent;
    }

    /// <summary>
    /// melonDS-android's own frontend contract (its README): ACTION_VIEW on an explicit
    /// EmulatorActivity component with the ROM's path in a PATH extra. ZIP and 7z are
    /// supported; the ROM must already be in melonDS's library or beside it for saves.
    /// </summary>
    private static Intent PathExtraIntent(string package, AndroidUri rom, RomLink link)
    {
        var intent = new Intent(Intent.ActionView);
        var activity = $"{package}.ui.emulator.EmulatorActivity";
        intent.SetComponent(new ComponentName(package, activity));
        intent.PutExtra("PATH", RomPathFor(link));
        intent.AddFlags(ActivityFlags.NewTask);
        return intent;
    }

    /// <summary>
    /// The generic handoff for emulators without a component-level contract: ACTION_VIEW
    /// with the ROM's URI and MIME, no package pin — Android's resolver picks the app
    /// (the player may get a chooser). When nothing resolves, the launcher opens the
    /// app's own library instead of failing silently.
    /// </summary>
    private static Intent ViewIntent(AndroidUri rom)
    {
        var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(rom, "application/octet-stream");
        intent.AddFlags(ActivityFlags.GrantReadUriPermission);
        return intent;
    }

    /// <summary>SAF ids are file://-shaped on primary storage; emulators only accept
    /// plain paths, so the id is handed over as-is.</summary>
    private static string RomPathFor(RomLink link) =>
        link.RomUri.StartsWith("file://", StringComparison.Ordinal)
            ? link.RomUri["file://".Length..]
            : link.RomUri;

    /// <summary>The core RetroArch pairs with the ROM's generation, inside its own
    /// package directory; null for DS (RetroArch's DS cores are opt-in) and unknowns,
    /// where RetroArch pairs the ROM on its own.</summary>
    private static string? CorePathHint(int generation, string package)
    {
        var coreFile = generation switch
        {
            1 or 2 => "gambatte_libretro_android.so",
            3 => "mgba_libretro_android.so",
            _ => null,
        };
        return coreFile is null ? null : $"/data/data/{package}/cores/{coreFile}";
    }

    /// <summary>The standard config per RetroArch build; null for forks that moved it.</summary>
    private static string? ConfigFileFor(string package) => package switch
    {
        "com.retroarch" => "/storage/emulated/0/Android/data/com.retroarch/files/retroarch.cfg",
        "com.retroarch.aarch64" => "/storage/emulated/0/Android/data/com.retroarch.aarch64/files/retroarch.cfg",
        "com.retroarch.ra32" => "/storage/emulated/0/Android/data/com.retroarch.ra32/files/retroarch.cfg",
        _ => null,
    };

    /// <summary>Starts the intent; false when no activity accepted it or the ROM grant failed.</summary>
    private static bool StartActivity(Activity activity, Intent intent, RomLink link)
    {
        bool started;
        try
        {
            intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
            activity.StartActivity(intent);
            started = true;
        }
        catch (ActivityNotFoundException)
        {
            started = false;
        }
        catch (Java.Lang.SecurityException)
        {
            started = false;
        }
        Services.AppLog.Info("launch", started
            ? $"Launched {link.RomFileName} in {SaveDescriptions.EmulatorName(link.Emulator)}."
            : $"No activity accepted the launch of {link.RomFileName} ({link.Emulator}).");
        return started;
    }
}
