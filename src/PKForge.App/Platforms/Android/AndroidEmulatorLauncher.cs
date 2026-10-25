using Android.App;
using Android.Content;
using Android.Content.PM;
using AndroidUri = Android.Net.Uri;
using PKForge.App.ViewModels;
using PKForge.Domain;

namespace PKForge.App;

/// <summary>
/// Hands a linked ROM to its emulator, the way each one documents it. Every launch
/// carries a URI: content:// ids from the SAF picker pass through, plain paths from
/// the ROM library become file:// URIs. RetroArch gets its full content-loading extra
/// set (EXTRA_CONTENT + RETROARCH_LOAD_CONTENT_PATH + EXTRA_CORE_PATH + the game
/// label), the same contract frontends use; standalone emulators get an ACTION_VIEW
/// with the ROM's MIME type. Emulators without a documented handoff open their own
/// library screen instead of a black void.
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

    /// <summary>The intent the emulator documents for a direct ROM handoff.</summary>
    private static Intent? BuildIntent(RomLaunchStyle style, string package, AndroidUri rom, RomLink link) => style switch
    {
        RomLaunchStyle.RetroArch => RetroArchIntent(package, rom, link),
        RomLaunchStyle.PathExtra => ViewIntent(package, rom),
        RomLaunchStyle.UriData => ViewIntent(package, rom),
        _ => null,
    };

    /// <summary>
    /// RetroArch's documented content-loading contract: MAIN + LAUNCHER with the ROM as
    /// data, plus the EXTRA_CONTENT / RETROARCH_LOAD_CONTENT_PATH pair, the game label
    /// and a core path hint. The core path names RetroArch's own core directory; when
    /// the file is absent RetroArch falls back to its own core pairing, so the game
    /// still loads rather than showing a black screen.
    /// </summary>
    private static Intent RetroArchIntent(string package, AndroidUri rom, RomLink link)
    {
        var intent = new Intent(Intent.ActionMain, rom);
        intent.SetPackage(package);
        intent.AddCategory(Intent.CategoryLauncher);
        intent.SetDataAndType(rom, "application/octet-stream");
        intent.PutExtra("RETROARCH_LOAD_CONTENT_PATH", rom.ToString());
        intent.PutExtra("EXTRA_CONTENT", rom.ToString());
        intent.PutExtra("EXTRA_GAME_LABEL", link.GameLabel);
        var core = CorePathHint(link.Generation, package);
        if (core is not null) intent.PutExtra("EXTRA_CORE_PATH", core);
        return intent;
    }

    /// <summary>The core RetroArch pairs with the ROM's generation, inside its own
    /// package directory (each RetroArch build owns its core dir; null lets RetroArch
    /// choose, which never black-screens).</summary>
    private static string? CorePathHint(int generation, string package)
    {
        if (generation is not (1 or 2 or 3)) return null; // DS cores are opt-in; RetroArch pairs on its own
        var coreFile = EmulatorCatalog.CoreForGeneration(generation) switch
        {
            RetroArchCore.Gambatte => "gambatte_libretro_android.so",
            RetroArchCore.MGba => "mgba_libretro_android.so",
            _ => null,
        };
        return coreFile is null ? null : $"/data/data/{package}/cores/{coreFile}";
    }

    /// <summary>The standalone emulators' contract: ACTION_VIEW with the ROM as data.</summary>
    private static Intent ViewIntent(string package, AndroidUri rom)
    {
        var intent = new Intent(Intent.ActionView);
        intent.SetPackage(package);
        intent.SetDataAndType(rom, "application/octet-stream");
        return intent;
    }

    /// <summary>Starts the intent; false when no activity accepted it or the ROM grant failed.</summary>
    private static bool StartActivity(Activity activity, Intent intent, RomLink link)
    {
        bool started;
        try
        {
            // The ROM grant rides along: Android re-grants read access to the target app.
            intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
            if (intent.ResolveActivity(activity.PackageManager!) is null)
            {
                Services.AppLog.Info("launch", $"No activity in the target emulator accepts the ROM handoff ({link.Emulator}).");
                return false;
            }
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
