using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using PKForge.App.ViewModels;
using PKForge.Domain;

namespace PKForge.App;

/// <summary>
/// The Android emulator launcher: resolves installed packages with PackageManager and
/// hands linked ROMs to emulators the way each one documents (RetroArch's explicit
/// RetroActivityFuture extras, melonDS's EmulatorActivity PATH extra, a VIEW intent
/// with a content URI for the rest). Emulators without a documented handoff open their
/// own library screen, and the status line says so instead of failing silently.
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
        var activity = Platform.CurrentActivity ?? throw new InvalidOperationException("No foreground Android activity is available.");
        var intent = BuildIntent(app.LaunchStyle, package, link);
        return await StartActivityAsync(activity, intent, link).ConfigureAwait(true);
    }

    public Task<bool> OpenAppAsync(EmulatorKind kind, CancellationToken cancellationToken = default)
    {
        var activity = Platform.CurrentActivity ?? throw new InvalidOperationException("No foreground Android activity is available.");
        var app = EmulatorCatalog.App(kind);
        var package = app.PackageIds.FirstOrDefault(IsInstalled) ?? app.PrimaryPackage;
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
            Services.AppLog.Info("launch", $"Could not open {SaveDescriptions.EmulatorName(kind)} ({package}): no launchable activity.");
        return Task.FromResult(opened);
    }

    /// <summary>The exact intent the emulator documents for a direct ROM handoff.</summary>
    private static Intent BuildIntent(RomLaunchStyle style, string package, RomLink link) => style switch
    {
        RomLaunchStyle.RetroArch => RetroArchIntent(package, link),
        RomLaunchStyle.PathExtra => EmulatorActivityIntent(package, link),
        RomLaunchStyle.UriData => ViewIntent(package, link),
        _ => ViewIntent(package, link),
    };

    private static Intent RetroArchIntent(string package, RomLink link)
    {
        // RetroArch runs everything from one activity; ROM and LIBRETRO name the content.
        // The activity class is com.retroarch.browser.retroactivity.RetroActivityFuture in
        // every build (com.retroarch, .aarch64, .ra32); only the package differs.
        var intent = new Intent();
        intent.SetComponent(new ComponentName(package, "com.retroarch.browser.retroactivity.RetroActivityFuture"));
        intent.SetAction(Intent.ActionMain);
        intent.AddCategory(Intent.CategoryLauncher);
        intent.AddFlags(ActivityFlags.NewTask);
        intent.PutExtra("ROM", RomPathFor(link));
        return intent;
    }

    private static Intent EmulatorActivityIntent(string package, RomLink link)
    {
        // melonDS's EmulatorActivity takes a PATH extra (its documented frontend contract).
        var intent = new Intent(Intent.ActionView);
        intent.SetComponent(new ComponentName(package, $"{package}.ui.emulator.EmulatorActivity"));
        intent.PutExtra("PATH", RomPathFor(link));
        intent.AddFlags(ActivityFlags.NewTask);
        return intent;
    }

    private static Intent ViewIntent(string package, RomLink link)
    {
        var intent = new Intent(Intent.ActionView);
        intent.SetPackage(package);
        intent.SetDataAndType(ParseUri(link.RomUri), "*/*");
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask | ActivityFlags.ClearTop);
        return intent;
    }

    /// <summary>
    /// SAF document ids are file://-shaped paths on primary storage; emulators on Android
    /// only accept plain file paths (they have no SAF bridge), so the id is handed over as-is.
    /// </summary>
    private static string RomPathFor(RomLink link) =>
        link.RomUri.StartsWith("file://", StringComparison.Ordinal)
            ? link.RomUri["file://".Length..]
            : link.RomUri;

    private static Android.Net.Uri ParseUri(string uri) => Android.Net.Uri.Parse(uri)
        ?? throw new InvalidOperationException($"The linked ROM URI could not be parsed: {uri}");

    private static Task<bool> StartActivityAsync(Activity activity, Intent intent, RomLink link)
    {
        bool started;
        try
        {
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
        return Task.FromResult(started);
    }
}
