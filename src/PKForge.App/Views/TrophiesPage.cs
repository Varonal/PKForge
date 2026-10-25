using System.Collections.ObjectModel;
using PKForge.App.Services;
using PKForge.App.Theme;
using PKForge.App.ViewModels;
using PKForge.Domain;

namespace PKForge.App.Views;

/// <summary>
/// The homemade trophies screen: one card per linked game with its completion percent
/// (tap or A for the detailed ladder), a Scan button that runs the same read path as the
/// Museum, and the full list per game — green when unlocked, muted while locked, with
/// the measured value at the last scan.
/// </summary>
public sealed class TrophiesPage : ContentPage, IPadHandler
{
    private readonly SavePickerViewModel _saves;
    private readonly TrophyScanService _scanner;
    private readonly ITrophyStore _trophies;
    private readonly ObservableCollection<GameTrophyCard> _cards = [];
    private VerticalStackLayout _rows = null!;
    private Grid _hostGrid = null!;
    private Label _readout = null!;
    private SecondScreenClaim? _secondClaim;

    public TrophiesPage(SavePickerViewModel saves, TrophyScanService scanner, ITrophyStore trophies)
    {
        _saves = saves;
        _scanner = scanner;
        _trophies = trophies;
        BindingContext = saves;
        Title = AppStrings.TrophiesTitle;
        BackgroundColor = UiTokens.Housing;
        NavigationPage.SetHasNavigationBar(this, false);

        var back = Kit.MiniCapsule(AppStrings.Back, UiTokens.Ink0);
        back.WidthRequest = 72;
        back.Clicked += async (_, _) => await Navigation.PopAsync();

        var header = Kit.HeaderBar("Trophies");
        header.VerticalOptions = LayoutOptions.Center;

        var scan = Kit.Capsule(AppStrings.ScanSaves, UiTokens.Cyan);
        scan.Clicked += (_, _) => _ = ScanAsync();

        var titleRow = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)],
            Children = { back, header, scan },
        };
        Grid.SetColumn(header, 1);
        Grid.SetColumn(scan, 2);

        _readout = new Label
        {
            Text = AppStrings.Fr ? "L'analyse parcourt chaque sauvegarde liée, comme le Musée. Les déblocages sont permanents." : "Scan runs over every linked save, the same reads as the Museum. Unlocks are permanent.",
            TextColor = UiTokens.InkSoft,
            FontFamily = DsChrome.PixelFont,
            FontSize = UiTokens.TextBody,
            LineBreakMode = LineBreakMode.WordWrap,
        };

        _rows = new VerticalStackLayout { Spacing = 8 };
        var scrollable = new ScrollView
        {
            Orientation = ScrollOrientation.Vertical,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = _rows,
        };

        var body = new Grid
        {
            RowSpacing = 10,
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Star)],
            Children = { Kit.Well(_readout, padding: 8), scrollable },
        };
        Grid.SetRow(scrollable, 1);

        var panel = Kit.DevicePanel(body);
        var root = new Grid
        {
            Padding = new Thickness(14, 10),
            RowSpacing = 10,
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Star)],
            Children = { titleRow, panel },
        };
        Grid.SetRow(panel, 1);
        _hostGrid = new Grid { Children = { DsChrome.GridBackground(), root } };
        Content = _hostGrid;
    }

    /// <summary>One game's overview card and its detailed ladder, built from the scan results.</summary>
    private sealed class GameTrophyCard
    {
        public required string GameLabel { get; init; }
        public required int Generation { get; init; }
        public required IReadOnlyList<TrophyDefinition> Definitions { get; init; }
        public required List<TrophyState> States { get; init; }
        public int Unlocked => States.Count(s => s.Unlocked);
        public int Total => Definitions.Count;
        public int Percent => Total == 0 ? 0 : (int)Math.Round(Unlocked * 100.0 / Total);
        public bool IsOpen;
    }

    private View BuildCard(GameTrophyCard card)
    {
        var title = new Label
        {
            Text = card.GameLabel,
            TextColor = UiTokens.Ink0,
            FontFamily = DsChrome.PixelFont,
            FontSize = 15,
            VerticalOptions = LayoutOptions.Center,
        };
        var percent = new Label
        {
            Text = $"{card.Percent}% · {card.Unlocked}/{card.Total}",
            TextColor = UiTokens.Cyan,
            FontFamily = DsChrome.PixelFont,
            FontSize = 13,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End,
        };
        var head = new Grid
        {
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)],
            Children = { title, percent },
        };
        Grid.SetColumn(percent, 1);

        var ladder = new VerticalStackLayout { Spacing = 4, IsVisible = card.IsOpen };
        foreach (var definition in card.Definitions)
            ladder.Children.Add(BuildTrophyRow(definition, card.States.FirstOrDefault(s => s.TrophyId == definition.Id)));

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => { card.IsOpen = !card.IsOpen; ladder.IsVisible = card.IsOpen; };
        var content = new VerticalStackLayout { Spacing = 6, Children = { head, ladder } };
        content.GestureRecognizers.Add(tap);
        return Kit.Well(content, padding: 10);
    }

    private View BuildTrophyRow(TrophyDefinition definition, TrophyState? state)
    {
        var unlocked = state?.Unlocked == true;
        var glyph = new Label
        {
            Text = definition.Glyph,
            FontSize = 14,
            VerticalOptions = LayoutOptions.Center,
            WidthRequest = 24,
            TextColor = unlocked ? UiTokens.Gold : UiTokens.Ink1,
            Opacity = unlocked ? 1.0 : 0.55,
        };
        var name = new Label
        {
            Text = definition.Title,
            TextColor = unlocked ? UiTokens.Ink0 : UiTokens.Ink1,
            FontFamily = DsChrome.PixelFont,
            FontSize = 13,
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
        };
        var value = new Label
        {
            Text = unlocked ? AppStrings.Unlocked : $"{state?.Current ?? 0}/{definition.Threshold}",
            TextColor = unlocked ? UiTokens.Green : UiTokens.InkSoft,
            FontFamily = DsChrome.PixelFont,
            FontSize = 12,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End,
        };
        var row = new Grid
        {
            ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)],
            ColumnSpacing = 6,
            Children = { glyph, name, value },
        };
        // Manual trophies unlock by the player's own say-so: the row confirms, never scans.
        if (definition.Kind == TrophyKind.Manual)
        {
            var tapRow = new TapGestureRecognizer();
            tapRow.Tapped += (_, _) => _ = ConfirmManualAsync(definition, state);
            row.GestureRecognizers.Add(tapRow);
        }
        return row;
    }

    /// <summary>The manual trophy flow: confirm to unlock, confirm again to lock it back.</summary>
    private async Task ConfirmManualAsync(TrophyDefinition definition, TrophyState? state)
    {
        if (state?.Unlocked == true)
        {
            var relock = await PadMenu.ConfirmAsync(_hostGrid, AppStrings.RelockQuestion(definition.Title),
                AppStrings.ManualTrophyRelockDetail, AppStrings.Relock);
            if (!relock) return;
            _trophies.Remove(definition.Id);
        }
        else
        {
            var unlock = await PadMenu.ConfirmAsync(_hostGrid, definition.Title, definition.Description, AppStrings.Fr ? "Débloquer" : "Unlock");
            if (!unlock) return;
            _trophies.Store(new TrophyState(definition.Id, true, 1));
        }
        Rebuild();
        PublishToSecondScreen();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Rebuild();
        _secondClaim ??= IPlatformApplication.Current?.Services.GetService<SecondScreenState>()?.Routes.CreateClaim(SecondScreenOwner.Trophies);
        _secondClaim?.Activate();
        PublishToSecondScreen();
        IPlatformApplication.Current?.Services.GetService<GamepadRouter>()?.Push(this);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _secondClaim?.Release();
        IPlatformApplication.Current?.Services.GetService<GamepadRouter>()?.Remove(this);
    }

    /// <summary>Feeds the highlighted game's ladder to the lower screen (a no-op without one).</summary>
    private void PublishToSecondScreen()
    {
        var state = IPlatformApplication.Current?.Services.GetService<SecondScreenState>();
        if (state is null) return;
        var card = _cards.Count > 0 ? _cards[_cards.Count - 1] : null;
        if (card is null)
        {
            state.TrophyLadder = null;
            return;
        }
        var rows = card.Definitions.Select(d =>
        {
            var trophyState = card.States.FirstOrDefault(s => s.TrophyId == d.Id);
            return (d.Title, d.Glyph, trophyState?.Unlocked == true,
                trophyState?.Unlocked == true ? "Unlocked" : $"{trophyState?.Current ?? 0}/{d.Threshold}");
        }).ToList();
        state.TrophyLadder = new TrophyLadder(card.GameLabel, card.Generation, card.Percent, card.Unlocked, card.Total, rows);
    }

    /// <summary>Rebuilds the overview from the store, keyed by the games the shelf shows.</summary>
    private void Rebuild()
    {
        _cards.Clear();
        _rows.Children.Clear();
        var states = _trophies.GetAll();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var save in _saves.Saves.Where(s => s.Identity?.IsHidden != true))
        {
            var label = save.EngineHint;
            if (!seen.Add(label)) continue;
            var definitions = new List<TrophyDefinition>(TrophyCatalog.ForGame(label));
            definitions.AddRange(TrophyManualCatalog.ForGame(label));
            if (definitions.Count == 0) continue;
            var gameStates = states.Where(s => definitions.Any(d => d.Id == s.TrophyId)).ToList();
            var card = new GameTrophyCard
            {
                GameLabel = label,
                Generation = save.Generation,
                Definitions = definitions,
                States = gameStates,
            };
            _cards.Add(card);
            _rows.Children.Add(BuildCard(card));
        }
    }

    private async Task ScanAsync()
    {
        if (_saves.IsBusy) return;
        _readout.Text = AppStrings.ScanningSaves;
        try
        {
            var facts = await _scanner.ScanAllAsync(_saves.Saves.ToList());
            _readout.Text = facts.Count == 0
                ? AppStrings.NoSaveScanned
                : AppStrings.ScannedSaves(facts.Count);
            Rebuild();
        }
        catch (Exception error)
        {
            _readout.Text = AppStrings.ScanFailed(error.Message);
        }
    }

    public bool OnPadButton(PadButton button)
    {
        switch (button)
        {
            case PadButton.A: _ = ScanAsync(); return true;
            case PadButton.B: _ = Navigation.PopAsync(); return true;
            default: return true; // own the pad while this page is up
        }
    }
}
