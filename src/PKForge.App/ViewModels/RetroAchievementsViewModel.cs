using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKForge.App.Services;
using PKForge.Domain;

namespace PKForge.App.ViewModels;

/// <summary>
/// The RetroAchievements screen's state: the signed-in player, one progress card per
/// linked Gen 1-5 save, and the achievement list of the game under the cursor. RA game
/// ids are resolved by title from the console's game list (cached for the session) so
/// the mapping survives RA-side renumbering.
/// </summary>
public partial class RetroAchievementsViewModel(
    IRetroCredentialStore credentials,
    IRetroAchievementsClient client,
    IRetroProgressStore cache) : ObservableObject
{
    private const string GameListUrl = "https://retroachievements.org/API/API_GetGameList.php";

    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(15),
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
    })
    { Timeout = TimeSpan.FromSeconds(30) };

    private readonly Dictionary<int, IReadOnlyList<(int Id, string Title, string Console)>> _consoleGames = [];

    [ObservableProperty] private string _status = "Sign in to RetroAchievements to see official achievements.";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _signedIn;

    public ObservableCollection<RetroGameCard> Games { get; } = [];

    public sealed partial class RetroGameCard : ObservableObject
    {
        public int GameId { get; set; }
        public string Title { get; set; } = "";
        public ObservableCollection<RetroAchievement> Achievements { get; set; } = [];
        public int Unlocked => Achievements.Count(a => a.Earned);
        public int Total => Achievements.Count;
        public int Percent => Total == 0 ? 100 : (int)Math.Round(Unlocked * 100.0 / Total);
    }

    public string? CurrentUsername => credentials.Load()?.Username;

    private void AddGame(Domain.RetroGameProgress game)
    {
        var card = new RetroGameCard { GameId = game.GameId, Title = game.Title };
        foreach (var achievement in game.Achievements) card.Achievements.Add(achievement);
        Games.Add(card);
    }

    [RelayCommand]
    public void SignOut()
    {
        credentials.Save(null);
        Games.Clear();
        SignedIn = false;
        Status = "Signed out. Official achievements stay on retroachievements.org.";
    }

    [RelayCommand]
    public async Task SignInAsync(string? apiKey)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(apiKey)) return;
        var username = CurrentUsername;
        if (string.IsNullOrWhiteSpace(username))
        {
            Status = "Enter your RetroAchievements username first.";
            return;
        }
        try
        {
            IsBusy = true;
            credentials.Save(new RetroCredentials(username.Trim(), apiKey.Trim()));
            if (await client.VerifyCredentialsAsync())
            {
                SignedIn = true;
                Status = $"Signed in as {username}.";
            }
            else
            {
                credentials.Save(null);
                Status = "RetroAchievements did not recognise those credentials.";
            }
        }
        catch (Exception error)
        {
            credentials.Save(null);
            Status = $"Sign-in failed: {error.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Sets the username before the API key is asked for.</summary>
    public void RememberUsername(string username) =>
        credentials.Save(new RetroCredentials(username.Trim(), credentials.Load()?.ApiKey ?? ""));

    /// <summary>One console's game list from RA, cached per session (RA asks for aggressive caching).</summary>
    private async Task<IReadOnlyList<(int Id, string Title, string Console)>> GameListAsync(int consoleId, CancellationToken token)
    {
        if (_consoleGames.TryGetValue(consoleId, out var cached)) return cached;
        var auth = credentials.Load() ?? throw new InvalidOperationException("Sign in to RetroAchievements first.");
        var url = $"{GameListUrl}?y={Uri.EscapeDataString(auth.ApiKey)}&i={consoleId}&f=1";
        using var response = await Http.GetAsync(url, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);
        var games = new List<(int, string, string)>();
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                if (!entry.TryGetProperty("ID", out var id) || !id.TryGetInt32(out var gameId)) continue;
                var title = entry.TryGetProperty("Title", out var t) ? t.GetString() ?? "" : "";
                games.Add((gameId, title, ""));
            }
        }
        var list = (IReadOnlyList<(int, string, string)>)games.AsReadOnly();
        _consoleGames[consoleId] = list;
        return list;
    }

    /// <summary>Resolves one linked save to its RA game id, by console and short title.</summary>
    private async Task<int?> ResolveGameIdAsync(DetectedSave save, CancellationToken token)
    {
        if (!RetroAchievementsRules.SupportsGeneration(save.Generation)) return null;
        var consoleId = RetroAchievementsRules.ConsoleForGeneration[save.Generation];
        var games = await GameListAsync(consoleId, token).ConfigureAwait(false);
        var shortName = RetroAchievementsRules.ShortName(save.EngineHint);
        var consoleName = consoleId switch
        {
            4 => "Game Boy", 5 => "Game Boy Color", 6 => "Game Boy Advance", 18 => "Nintendo DS", _ => "",
        };
        foreach (var (id, title, _) in games)
            if (RetroAchievementsRules.TitleMatches(title, consoleName, shortName)) return id;
        return null;
    }

    /// <summary>Refreshes every linked Gen 1-5 save's official achievement set.</summary>
    public async Task RefreshForSavesAsync(IReadOnlyList<DetectedSave> saves, CancellationToken cancellationToken = default)
    {
        if (!SignedIn)
        {
            Games.Clear();
            return;
        }
        try
        {
            IsBusy = true;
            Games.Clear();
            var seen = new HashSet<int>();
            foreach (var save in saves.Where(s => !s.Identity?.IsHidden == true && RetroAchievementsRules.SupportsGeneration(s.Generation)))
            {
                var gameId = await ResolveGameIdAsync(save, cancellationToken).ConfigureAwait(false);
                if (gameId is null || !seen.Add(gameId.Value)) continue;
                var progress = await client.GetGameProgressAsync(gameId.Value, cancellationToken).ConfigureAwait(false);
                var view = new RetroGameCard { GameId = progress.GameId, Title = progress.Title };
                foreach (var achievement in progress.Achievements) view.Achievements.Add(achievement);
                Games.Add(view);
            }
            Status = Games.Count == 0
                ? "No official achievement sets found for your Gen 1-5 games."
                : $"{Games.Count} game set(s) loaded.";
        }
        catch (Exception error)
        {
            Status = $"Could not reach RetroAchievements: {error.Message}. Cached progress is shown.";
            foreach (var progress in cache.GetAll())
                AddGame(progress);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Fills the view from the offline cache, so the screen never opens empty.</summary>
    public void LoadCached()
    {
        if (Games.Count > 0) return;
        foreach (var progress in cache.GetAll())
            AddGame(progress);
    }
}
