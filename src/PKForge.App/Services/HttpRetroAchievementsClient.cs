using System.Text.Json;
using PKForge.Domain;

namespace PKForge.App.Services;

/// <summary>
/// The RetroAchievements web API client (API_GetGameInfoAndUserProgress), following the
/// same HTTP shape as AppUpdateService: one shared client, short timeouts, and errors
/// thrown as messages the status line can show. Credentials live in the platform's
/// secure storage and are never written to plain files or logs.
/// </summary>
public sealed class HttpRetroAchievementsClient(
    IRetroCredentialStore credentials,
    IRetroProgressStore cache) : IRetroAchievementsClient
{
    private const string BaseUrl = "https://retroachievements.org/API/API_GetGameInfoAndUserProgress.php";
    private const string ProfileUrl = "https://retroachievements.org/API/API_GetUserSummary.php";

    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(15),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    })
    { Timeout = TimeSpan.FromSeconds(30) };

    private RetroCredentials Credentials =>
        credentials.Load() ?? throw new InvalidOperationException("Sign in to RetroAchievements first.");

    public async Task<bool> VerifyCredentialsAsync(CancellationToken cancellationToken = default)
    {
        var auth = Credentials;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var url = $"{ProfileUrl}?y={Uri.EscapeDataString(auth.ApiKey)}&u={Uri.EscapeDataString(auth.Username)}&g=1";
        using var response = await Http.GetAsync(url, timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"RetroAchievements answered {response.StatusCode}.");
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);
        // An unknown user comes back as an error object, a valid one as a profile with an ID.
        return document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("ID", out _);
    }

    public async Task<RetroGameProgress> GetGameProgressAsync(int gameId, CancellationToken cancellationToken = default)
    {
        if (gameId <= 0) throw new ArgumentOutOfRangeException(nameof(gameId));
        var auth = Credentials;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var url = $"{BaseUrl}?y={Uri.EscapeDataString(auth.ApiKey)}&u={Uri.EscapeDataString(auth.Username)}&g={gameId}&a=1";
        using var response = await Http.GetAsync(url, timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"RetroAchievements answered {response.StatusCode} for game {gameId}.");
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("ID", out _))
            throw new InvalidOperationException("RetroAchievements did not recognise that game.");

        var title = root.TryGetProperty("Title", out var titleElement) ? titleElement.GetString() ?? string.Empty : string.Empty;
        var achievements = new List<RetroAchievement>();
        if (root.TryGetProperty("Achievements", out var set) && set.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in set.EnumerateObject())
            {
                var a = entry.Value;
                var id = a.TryGetProperty("ID", out var idElement) && idElement.TryGetInt32(out var parsedId) ? parsedId : 0;
                var earned = a.TryGetProperty("DateEarned", out var dateEarned) && dateEarned.ValueKind == JsonValueKind.String;
                var earnedHardcore = a.TryGetProperty("DateEarnedHardcore", out var dateHardcore) && dateHardcore.ValueKind == JsonValueKind.String;
                achievements.Add(new RetroAchievement(
                    id,
                    a.TryGetProperty("Title", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                    a.TryGetProperty("Description", out var description) ? description.GetString() ?? string.Empty : string.Empty,
                    a.TryGetProperty("Points", out var points) && points.TryGetInt32(out var pointValue) ? pointValue : 0,
                    a.TryGetProperty("BadgeName", out var badge) ? badge.GetString() ?? string.Empty : string.Empty,
                    earned,
                    earned && ParseTimestamp(a, "DateEarned"),
                    earnedHardcore));
            }
        }

        var progress = new RetroGameProgress(gameId, title, [.. achievements.OrderBy(a => a.Id)]);
        cache.Store(progress);
        return progress;
    }

    /// <summary>RA's "YYYY-MM-DD HH:mm:ss" timestamps, treated as UTC (the site's own clock).</summary>
    private static DateTimeOffset? ParseTimestamp(JsonElement achievement, string property)
    {
        if (!achievement.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String) return null;
        return DateTimeOffset.TryParse(
            value.GetString(),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var parsed) ? parsed : null;
    }
}

/// <summary>
/// <see cref="IRetroCredentialStore"/> over MAUI's SecureStorage: the API key is a
/// password-grade secret, so it never reaches plain Preferences or a JSON file.
/// </summary>
public sealed class SecureRetroCredentialStore : IRetroCredentialStore
{
    private const string UserKey = "ra_username";
    private const string KeyKey = "ra_api_key";

    public RetroCredentials? Load()
    {
        var user = Preferences.Default.Get(UserKey, string.Empty);
        var key = SecureStorage.Default.GetAsync(KeyKey).GetAwaiter().GetResult();
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(key)) return null;
        return new RetroCredentials(user, key);
    }

    public void Save(RetroCredentials? credentials)
    {
        if (credentials is null)
        {
            Preferences.Default.Remove(UserKey);
            SecureStorage.Default.Remove(KeyKey);
            return;
        }
        Preferences.Default.Set(UserKey, credentials.Username);
        SecureStorage.Default.SetAsync(KeyKey, credentials.ApiKey).GetAwaiter().GetResult();
    }
}
