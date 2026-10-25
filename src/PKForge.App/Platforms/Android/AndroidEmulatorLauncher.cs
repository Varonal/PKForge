using Android.App;
using Android.Content;
using Android.Content.PM;
using AndroidUri = Android.Net.Uri;
using PKForge.App.ViewModels;
using PKForge.Domain;

namespace PKForge.App;

/// <summary>
/// Hands a linked ROM to its emulator, PlayHub-style: package-scoped intents, never
/// explicit components (an explicit component that does not match the installed build
/// throws SecurityException and the launch dies silently). RetroArch gets a MAIN intent
/// scoped to its package with the ROM as data plus its content-loading extras; every
/// standalone emulator gets an ACTION_VIEW scoped to its package with the ROM URI and
/// a MIME type, resolved first — when nothing resolves, the emulator's own library
/// opens with a clear message instead of a black screen.
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

        // One URI per launch: content ids pass through, plain paths become file://.
        var rom = ResolveUri(link.RomUri);
        if (rom is null)
        {
            Services.AppLog.Info("launch", $"The linked ROM cannot be read: {link.RomUri}");
            return false;
        }

        var activity = Platform.CurrentActivity ?? throw new InvalidOperationException("No foreground Android activity is available.");
        var context = activity.ApplicationContext ?? (Android.Content.Context)activity;
        var intent = BuildIntent(app.LaunchStyle, package, rom, link);
        if (intent is null) return await OpenAppAsync(link.Emulator, cancellationToken).ConfigureAwait(true);

        // Nothing in the target package answers this intent: open its library instead
        // of a dead screen, and say so.
        if (intent.ResolveActivity(context.PackageManager!) is null)
        {
            Services.AppLog.Info("launch", $"{package} has no activity for the ROM handoff; opening its library.");
            return await OpenAppAsync(link.Emulator, cancellationToken).ConfigureAwait(true);
        }
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

    /// <summary>
    /// content:// ids pass through; plain file paths become FileProvider content://
    /// URIs — the only URI kind Android can grant another app read access to
    /// (FLAG_GRANT_READ_URI_PERMISSION does nothing on file:// URIs, which is why the
    /// emulators opened but could not load the game).
    /// </summary>
    private static AndroidUri? ResolveUri(string romUri)
    {
        if (romUri.StartsWith("content://", StringComparison.Ordinal))
            return AndroidUri.Parse(romUri);
        try
        {
            if (!File.Exists(romUri)) return null;
            return AndroidX.Core.Content.FileProvider.GetUriForFile(
                Platform.AppContext!, "org.pkforge.app.romprovider", new Java.IO.File(romUri));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The package-scoped intent per launch style. No explicit components:
    /// the emulator's own manifest decides which activity answers.</summary>
    private static Intent? BuildIntent(RomLaunchStyle style, string package, AndroidUri rom, RomLink link) => style switch
    {
        RomLaunchStyle.RetroArch => RetroArchIntent(package, rom, link),
        RomLaunchStyle.PathExtra => PathExtraIntent(package, rom, link),
        RomLaunchStyle.UriData => ViewIntent(package, rom),
        _ => null,
    };

    /// <summary>
    /// RetroArch's content-loading contract: a MAIN intent scoped to its package with
    /// the ROM as data and MIME, plus the EXTRA_CONTENT / RETROARCH_LOAD_CONTENT_PATH
    /// pair and a core path hint. Package scoping (never a component) is what frontends
    /// like Pegasus use; the extras make RetroArch load the game directly.
    /// </summary>
    private static Intent RetroArchIntent(string package, AndroidUri rom, RomLink link)
    {
        // The libretro CLI contract, exactly: "Only ROM and LIBRETRO are needed".
        // A plain ROM path in the ROM extra; no core hint (RetroArch pairs the core
        // from its own list — a wrong core path black-screens), no config file.
        var romPath = RomPathFor(link);
        var intent = new Intent();
        intent.SetPackage(package);
        intent.SetAction(Intent.ActionMain);
        intent.AddCategory(Intent.CategoryLauncher);
        intent.PutExtra("ROM", romPath);
        // LIBRETRO only when the generation's core is named: a null extra would make
        // RetroArch load nothing, so unknowns let RetroArch pair the core itself.
        var core = CorePathHint(link.Generation, package);
        if (core is not null) intent.PutExtra("LIBRETRO", core);
        return intent;
    }

    /// <summary>
    /// melonDS-android's verified frontend configuration (the Beacon/Pegasus setups):
    /// ACTION_VIEW with the explicit EmulatorActivity component, the ROM as data, a
    /// "uri" string extra with the URI, and a "PATH" string extra with the plain path —
    /// all four together. The ROM must appear in melonDS's own library first (its
    /// scanner); without it, melonDS opens but cannot load the game.
    /// </summary>
    private static Intent PathExtraIntent(string package, AndroidUri rom, RomLink link)
    {
        var intent = new Intent(Intent.ActionView);
        intent.SetComponent(new ComponentName(package, $"{package}.ui.emulator.EmulatorActivity"));
        intent.SetDataAndType(rom, "application/octet-stream");
        intent.PutExtra("uri", rom.ToString());
        intent.PutExtra("PATH", RomPathFor(link));
        intent.AddFlags(ActivityFlags.GrantReadUriPermission);
        return intent;
    }

    /// <summary>The generic handoff: ACTION_VIEW scoped to the package, ROM as data.
    /// The yuzu family (Eden, Sudachi) and the Citra family (Azahar) load the game
    /// from the data URI through their game-file viewer; when the build prefers its
    /// own library screen, the resolver decides and the game opens from there.</summary>
    private static Intent ViewIntent(string package, AndroidUri rom)
    {
        var intent = new Intent(Intent.ActionView);
        intent.SetPackage(package);
        intent.SetDataAndType(rom, "application/octet-stream");
        return intent;
    }

    /// <summary>SAF ids are file://-shaped on primary storage; emulators only accept
    /// plain paths, so the id is handed over as-is.</summary>
    private static string RomPathFor(RomLink link) =>
        link.RomUri.StartsWith("file://", StringComparison.Ordinal)
            ? link.RomUri["file://".Length..]
            : link.RomUri;

    /// <summary>The core RetroArch pairs with the ROM's generation, inside its own
    /// package directory; null for DS and unknowns, where RetroArch pairs on its own.</summary>
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
