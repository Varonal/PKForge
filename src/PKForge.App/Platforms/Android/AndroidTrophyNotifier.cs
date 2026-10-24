using Android.App;
using AndroidX.Core.App;
using PKForge.App.Services;
using PKForge.Domain;

namespace PKForge.App;

/// <summary>
/// The Android trophy notification: one channel, one notification per scan announcing
/// every newly unlocked trophy (one line each, up to three, then a count). Requires the
/// POST_NOTIFICATIONS runtime permission on Android 13+, requested by the Trophies page.
/// </summary>
public sealed class AndroidTrophyNotifier : Java.Lang.Object, Services.ITrophyNotifier
{
    private const string ChannelId = "pkforge_trophies";
    private const int NotificationId = 4110;

    public Task NotifyAsync(IReadOnlyList<TrophyDefinition> unlocked)
    {
        ArgumentNullException.ThrowIfNull(unlocked);
        if (unlocked.Count == 0) return Task.CompletedTask;
        var context = Platform.AppContext
            ?? throw new InvalidOperationException("No Android application context is available.");
        EnsureChannel(context);

        var manager = NotificationManagerCompat.From(context)
            ?? throw new InvalidOperationException("NotificationManagerCompat is unavailable.");
        if (!manager.AreNotificationsEnabled()) return Task.CompletedTask;

        var first = unlocked[0];
        var summary = unlocked.Count == 1
            ? $"{first.Glyph} {first.Title}"
            : $"{unlocked.Count} trophies unlocked, starting with {first.Title}";
        var detail = string.Join("\n", unlocked.Take(3).Select(t => $"{t.Glyph} {t.Title} · {t.GameLabel}"));

        // The AndroidX binding annotates every setter's return as nullable, so the
        // notification is built statement by statement instead of as a fluent chain.
        var builder = new NotificationCompat.Builder(context, ChannelId)!;
        builder.SetSmallIcon(Resource.Drawable.trophy_notification);
        builder.SetContentTitle("PKForge trophies");
        builder.SetContentText(summary);
        builder.SetStyle(new NotificationCompat.BigTextStyle()!.BigText(detail));
        builder.SetAutoCancel(true);

        try
        {
            manager.Notify(NotificationId, builder.Build());
        }
        catch (Exception error)
        {
            Services.AppLog.Info("trophies", $"The trophy notification could not be shown: {error.Message}");
        }
        return Task.CompletedTask;
    }

    private static void EnsureChannel(Android.Content.Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;
        var manager = (NotificationManager)context.GetSystemService(Android.Content.Context.NotificationService)!;
        if (manager.GetNotificationChannel(ChannelId) is not null) return;
        var channel = new NotificationChannel(ChannelId, "Trophies", NotificationImportance.Default)
        {
            Description = "Homemade trophy unlocks, detected from save scans.",
        };
        manager.CreateNotificationChannel(channel);
    }
}
