using System.Text.Json;

namespace PKForge.Domain;

/// <summary>The player's RetroAchievements credentials. The API key is a secret: it is
/// stored in the platform's secure storage, never in plain files.</summary>
public sealed record RetroCredentials(string Username, string ApiKey);

/// <summary>Reads and stores RetroAchievements credentials securely.</summary>
public interface IRetroCredentialStore
{
    RetroCredentials? Load();

    /// <summary>Stores the credentials, or clears them when null.</summary>
    void Save(RetroCredentials? credentials);
}

/// <summary>
/// Parses the RetroAchievements web API's JSON, in isolation from HTTP so the mapping
/// is unit tested. Follows API_GetGameInfoAndUserProgress: an achievement carries
/// DateEarned/DateEarnedHardcore only once unlocked, but older API responses returned
/// an empty string for a locked one - presence alone is never proof of an unlock.
/// </summary>
public static class RetroAchievementsJson
{
    public static bool LooksLikeGame(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("ID", out _);

    public static bool LooksLikeProfile(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("ID", out _);

    public static RetroGameProgress ParseGameProgress(JsonElement root, int requestedGameId)
    {
        var title = root.TryGetProperty("Title", out var titleElement) ? titleElement.GetString() ?? string.Empty : string.Empty;
        var achievements = new List<RetroAchievement>();
        if (root.TryGetProperty("Achievements", out var set) && set.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in set.EnumerateObject())
            {
                var a = entry.Value;
                var id = a.TryGetProperty("ID", out var idElement) && idElement.TryGetInt32(out var parsedId) ? parsedId : 0;
                var earned = IsEarned(a, "DateEarned");
                var earnedHardcore = IsEarned(a, "DateEarnedHardcore");
                achievements.Add(new RetroAchievement(
                    id,
                    a.TryGetProperty("Title", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                    a.TryGetProperty("Description", out var description) ? description.GetString() ?? string.Empty : string.Empty,
                    a.TryGetProperty("Points", out var points) && points.TryGetInt32(out var pointValue) ? pointValue : 0,
                    a.TryGetProperty("BadgeName", out var badge) ? badge.GetString() ?? string.Empty : string.Empty,
                    earned,
                    earned ? ParseTimestamp(a, "DateEarned") : null,
                    earnedHardcore));
            }
        }
        return new RetroGameProgress(requestedGameId, title, [.. achievements.OrderBy(a => a.Id)]);
    }

    private static bool IsEarned(JsonElement achievement, string property) =>
        achievement.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString());

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

/// <summary>One official RetroAchievements achievement, as PKForge shows it.</summary>
public sealed record RetroAchievement(
    int Id,
    string Title,
    string Description,
    int Points,
    /// <summary>The badge URL on media.retroachievements.org; PKForge prefixes the host.</summary>
    string BadgeName,
    bool Earned,
    /// <summary>When the player earned it (softcore or hardcore), null while locked.</summary>
    DateTimeOffset? DateEarned,
    /// <summary>True when the unlock is a hardcore unlock (worth double points).</summary>
    bool EarnedHardcore);

/// <summary>One game's RetroAchievements set and the player's progress in it.</summary>
public sealed record RetroGameProgress(
    int GameId,
    string Title,
    IReadOnlyList<RetroAchievement> Achievements)
{
    public int Unlocked => Achievements.Count(a => a.Earned);

    public int Total => Achievements.Count;

    /// <summary>Completion percentage, whole points, 100 when the set is empty (nothing to do).</summary>
    public int Percent => Total == 0 ? 100 : (int)Math.Round(Unlocked * 100.0 / Total);

    public int PointsEarned => Achievements.Where(a => a.Earned).Sum(a => a.Points);

    public int PointsTotal => Achievements.Sum(a => a.Points);
}

/// <summary>Talks to the RetroAchievements web API (API_GetGameInfoAndUserProgress).</summary>
public interface IRetroAchievementsClient
{
    /// <summary>Fetches one game's achievement set with the player's unlock state.</summary>
    Task<RetroGameProgress> GetGameProgressAsync(int gameId, CancellationToken cancellationToken = default);

    /// <summary>Verifies the credentials with a cheap call; throws on bad credentials.</summary>
    Task<bool> VerifyCredentialsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Persists the last fetched progress per game, so the awards view opens offline.</summary>
public interface IRetroProgressStore
{
    IReadOnlyList<RetroGameProgress> GetAll();

    RetroGameProgress? Find(int gameId);

    void Store(RetroGameProgress progress);
}

/// <summary>
/// Maps PKForge saves to RetroAchievements games. The RA consoles are GB (4), GBC (5),
/// GBA (6) and NDS (18); the resolution runs over the user's fetched game list, so the
/// table only names how PKForge labels map to RA titles, never hard-coded RA game ids.
/// </summary>
public static class RetroAchievementsRules
{
    /// <summary>RetroAchievements console ids for the generations PKForge supports.</summary>
    public static readonly IReadOnlyDictionary<int, int> ConsoleForGeneration =
        new Dictionary<int, int> { [1] = 4, [2] = 5, [3] = 6, [4] = 18, [5] = 18 };

    /// <summary>True when a game's generation has an official RA set (Gen 1-5).</summary>
    public static bool SupportsGeneration(int generation) => ConsoleForGeneration.ContainsKey(generation);

    /// <summary>Extracts the short game name ("Emerald", "Black 2") from a PKForge game label.</summary>
    public static string ShortName(string gameLabel) => gameLabel
        .Replace("Pokémon ", "", StringComparison.OrdinalIgnoreCase)
        .Replace("Pokemon ", "", StringComparison.OrdinalIgnoreCase)
        .Replace(" Version", "", StringComparison.OrdinalIgnoreCase)
        .Trim();

    /// <summary>
    /// True when an RA game title is the set the player wants, for the console RA lists
    /// it on: the edition must match ("Pokémon X" never answers to "Pokémon Y") and the
    /// shared-set titles (HeartGold|SoulSilver) match either edition.
    /// </summary>
    public static bool TitleMatches(string raTitle, string consoleName, string shortName)
    {
        if (string.IsNullOrWhiteSpace(shortName)) return false;
        var parts = raTitle.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Any(part => TitlesEqual(part, shortName));
    }

    private static bool TitlesEqual(string raTitle, string shortName)
    {
        // "Pokémon Emerald Version" vs "Emerald": RA titles keep the "Version" suffix,
        // and Gen 1 titles are split into per-edition sets ("Red Version").
        var a = Normalise(raTitle);
        var b = Normalise(shortName);
        return a == b
            || (a.StartsWith(b + " ", StringComparison.Ordinal) && a.EndsWith(" version", StringComparison.Ordinal))
            || (b.StartsWith(a + " ", StringComparison.Ordinal) && b.EndsWith(" version", StringComparison.Ordinal));

        static string Normalise(string title)
        {
            var lower = title.ToLowerInvariant().Trim();
            foreach (var prefix in new[] { "pokemon ", "pokémon " })
                if (lower.StartsWith(prefix, StringComparison.Ordinal)) lower = lower[prefix.Length..].TrimStart();
            return lower.Replace(" version", "", StringComparison.Ordinal).Trim();
        }
    }
}
