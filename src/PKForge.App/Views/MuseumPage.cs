using PKForge.App.Services;
using PKForge.App.Theme;
using PKForge.App.ViewModels;
using PKForge.Chrome;
using PKForge.Domain;

namespace PKForge.App.Views;

/// <summary>Read-only Pokédex archive, one entry per linked save. The game flags come from
/// that save; shiny marks come from Pokémon currently stored in its boxes or party.</summary>
public sealed class MuseumPage : ContentPage, IPadHandler
{
    private sealed record MuseumGame(
        DetectedSave Save,
        IReadOnlyList<int> Species,
        IReadOnlyDictionary<int, DexEntryState> Entries,
        IReadOnlySet<int> ShinySpecies,
        int Seen,
        int Caught,
        string? Error)
    {
        public int Total => Species.Count;
        public bool CanOpen => Error is null && Total > 0;
        public int ShinyCount => ShinySpecies.Count;
    }

    private readonly SavePickerViewModel _saves;
    private readonly ISaveFileAccess _files;
    private readonly ISaveEngine _engine;
    private readonly IGameDataService _data;
    private readonly ISpriteService _sprites;
    private readonly GamepadRouter _router;
    private readonly SecondScreenState _secondScreen;
    private readonly List<MuseumGame> _games = [];
    private readonly List<Border> _gameRows = [];
    private readonly Label _readout;
    private readonly VerticalStackLayout _rows;
    private readonly ScrollView _scroll;
    private readonly Button _refresh;
    private readonly Grid _hostGrid;
    private SecondScreenClaim? _pageClaim;
    private SecondScreenClaim? _dexClaim;
    private DexGridView? _dexGrid;
    private Grid? _dexOverlay;
    private Label? _dexProgress;
    private Label? _dexCursorInfo;
    private CancellationTokenSource? _scanCancellation;
    private int _selectedRow;
    private bool _scanInProgress;

    public MuseumPage(SavePickerViewModel saves, ISaveFileAccess files, ISaveEngine engine,
        IGameDataService data, ISpriteService sprites, GamepadRouter router, SecondScreenState secondScreen)
    {
        _saves = saves;
        _files = files;
        _engine = engine;
        _data = data;
        _sprites = sprites;
        _router = router;
        _secondScreen = secondScreen;

        Title = AppStrings.MuseumTitle;
        BackgroundColor = UiTokens.Housing;
        NavigationPage.SetHasNavigationBar(this, false);

        var back = Kit.MiniCapsule(AppStrings.Back, UiTokens.Ink0);
        back.WidthRequest = 72;
        back.Clicked += async (_, _) => await Navigation.PopAsync();
        var header = Kit.HeaderBar(AppStrings.MuseumTitle);
        header.VerticalOptions = LayoutOptions.Center;
        _refresh = Kit.Capsule(AppStrings.MuseumScan, UiTokens.Cyan);
        _refresh.Clicked += (_, _) => _ = ScanAsync();

        var titleRow = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)],
            Children = { back, header, _refresh },
        };
        Grid.SetColumn(header, 1);
        Grid.SetColumn(_refresh, 2);

        _readout = new Label
        {
            Text = AppStrings.MuseumReadOnly,
            TextColor = UiTokens.InkSoft,
            FontFamily = DsChrome.PixelFont,
            FontSize = UiTokens.TextBody,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        _rows = new VerticalStackLayout { Spacing = 3 };
        _scroll = new ScrollView
        {
            Orientation = ScrollOrientation.Vertical,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = _rows,
        };

        var hints = Kit.WindowHints(
            ("A", AppStrings.Open, () => OpenSelectedDex()),
            ("B", AppStrings.Back, () => _ = Navigation.PopAsync()),
            ("X", AppStrings.MuseumScan, () => _ = ScanAsync()));
        var body = new Grid
        {
            RowSpacing = 8,
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)],
            Children = { Kit.Well(_readout, padding: 8), _scroll, hints },
        };
        Grid.SetRow(_scroll, 1);
        Grid.SetRow(hints, 2);

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

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _secondScreen.PreviewSpecies = null;
        _pageClaim ??= _secondScreen.Routes.CreateClaim(SecondScreenOwner.Pokedex);
        _pageClaim.Activate();
        _router.Push(this);
        if (_games.Count == 0) _ = ScanAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _scanCancellation?.Cancel();
        CloseDexOverlay();
        _pageClaim?.Release();
        _router.Remove(this);
    }

    private async Task ScanAsync()
    {
        if (_scanInProgress) return;
        _scanInProgress = true;
        _refresh.IsEnabled = false;
        _readout.Text = AppStrings.MuseumReading;
        var cancellation = new CancellationTokenSource();
        _scanCancellation = cancellation;
        var token = cancellation.Token;
        try
        {
            var saves = _saves.Saves.Where(save => save.Identity?.IsHidden != true).ToArray();
            var measured = new List<MuseumGame>(saves.Length);
            foreach (var save in saves)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var bytes = await _files.ReadAsync(save.DocumentId, token).ConfigureAwait(false);
                    measured.Add(await Task.Run(() => Measure(save, bytes), token).ConfigureAwait(false));
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception error)
                {
                    AppLog.Error("museum", $"Reading {save.GameLabel} ({save.FileName}) failed", error);
                    measured.Add(Failed(save, error));
                }
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (token.IsCancellationRequested) return;
                _games.Clear();
                _games.AddRange(measured);
                RebuildRows();
                _readout.Text = saves.Length == 0
                    ? AppStrings.MuseumNoSaves
                    : AppStrings.MuseumReadOnly;
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Leaving the page cancels the scan; the next visit starts a fresh read.
        }
        finally
        {
            cancellation.Dispose();
            if (ReferenceEquals(_scanCancellation, cancellation)) _scanCancellation = null;
            _scanInProgress = false;
            await MainThread.InvokeOnMainThreadAsync(() => _refresh.IsEnabled = true);
        }
    }

    private MuseumGame Measure(DetectedSave save, ReadOnlyMemory<byte> bytes)
    {
        using var session = _engine.OpenSession(bytes, save.EngineHint, save.Format);
        var maxSpecies = Math.Min(session.MaxSpeciesId, _data.SpeciesNames.Count - 1);
        var species = new List<int>(Math.Max(0, maxSpecies));
        var entries = new Dictionary<int, DexEntryState>();
        for (var id = 1; id <= maxSpecies; id++)
        {
            if (_data.SpeciesNames[id].Length == 0 || !session.IsDexSpecies(id)) continue;
            species.Add(id);
            entries[id] = session.GetDexEntry(id);
        }

        var shiny = session.Snapshot.Slots
            .Where(slot => slot.Species is > 0 && !slot.IsEgg && slot.IsShiny)
            .Select(slot => slot.Species!.Value)
            .Where(entries.ContainsKey)
            .ToHashSet();
        return new MuseumGame(save, species, entries, shiny,
            entries.Values.Count(entry => entry.Seen),
            entries.Values.Count(entry => entry.Caught),
            Error: species.Count == 0 ? AppStrings.MuseumNoDex : null);
    }

    private static MuseumGame Failed(DetectedSave save, Exception error) =>
        new(save, [], new Dictionary<int, DexEntryState>(), new HashSet<int>(), 0, 0,
            AppStrings.MuseumReadError(error.Message));

    private void RebuildRows()
    {
        _rows.Children.Clear();
        _gameRows.Clear();
        _selectedRow = Math.Clamp(_selectedRow, 0, Math.Max(0, _games.Count - 1));
        if (_games.Count == 0)
        {
            _rows.Children.Add(Kit.BodyText(AppStrings.MuseumNoSaves, UiTokens.InkSoft));
            return;
        }

        for (var i = 0; i < _games.Count; i++)
        {
            var game = _games[i];
            var titleText = game.Save.Identity?.DisplayName is { Length: > 0 } customName
                ? customName
                : game.Save.EngineHint;
            var title = new Label
            {
                Text = titleText,
                TextColor = UiTokens.Ink0,
                FontFamily = DsChrome.PixelFont,
                FontSize = 14,
                VerticalOptions = LayoutOptions.Center,
                LineBreakMode = LineBreakMode.TailTruncation,
            };
            var detail = new Label
            {
                Text = game.Error ?? AppStrings.MuseumProgress(game.Seen, game.Caught, game.Total, game.ShinyCount),
                TextColor = game.Error is null ? UiTokens.InkSoft : UiTokens.Gold,
                FontFamily = DsChrome.PixelFont,
                FontSize = UiTokens.TextSmall,
                LineBreakMode = LineBreakMode.TailTruncation,
            };
            var filename = Kit.Caption(game.Save.FileName);
            var text = new VerticalStackLayout { Spacing = 2, Children = { title, detail, filename } };
            var row = Kit.Row(text, shaded: i % 2 != 0, padding: new Thickness(10, 7));
            var tap = new TapGestureRecognizer();
            var index = i;
            tap.Tapped += (_, _) =>
            {
                SelectRow(index);
                OpenSelectedDex();
            };
            row.GestureRecognizers.Add(tap);
            _rows.Children.Add(row);
            _gameRows.Add(row);
        }
        SelectRow(_selectedRow, scrollIntoView: false);
    }

    private void SelectRow(int index, bool scrollIntoView = true)
    {
        if (_gameRows.Count == 0) return;
        _selectedRow = Math.Clamp(index, 0, _gameRows.Count - 1);
        for (var i = 0; i < _gameRows.Count; i++)
            Kit.SetRowFocus(_gameRows[i], i == _selectedRow, shaded: i % 2 != 0);
        if (scrollIntoView) _ = _scroll.ScrollToAsync(_gameRows[_selectedRow], ScrollToPosition.MakeVisible, true);
    }

    private void OpenSelectedDex()
    {
        if (_dexOverlay is not null || (uint)_selectedRow >= (uint)_games.Count) return;
        var game = _games[_selectedRow];
        if (!game.CanOpen)
        {
            _readout.Text = game.Error ?? AppStrings.MuseumNoDex;
            return;
        }

        var ids = game.Species;
        var grid = new DexGridView(_sprites) { VerticalOptions = LayoutOptions.Fill };
        _dexGrid = grid;
        _dexProgress = new Label
        {
            TextColor = UiTokens.Ink1,
            FontFamily = DsChrome.PixelFont,
            FontSize = UiTokens.TextBody,
            HorizontalTextAlignment = TextAlignment.End,
            HorizontalOptions = LayoutOptions.End,
        };
        _dexCursorInfo = new Label
        {
            TextColor = UiTokens.Ink1,
            FontFamily = DsChrome.PixelFont,
            FontSize = UiTokens.TextBody,
        };
        var title = new Label
        {
            Text = game.Save.Identity?.DisplayName is { Length: > 0 } customName ? customName : game.Save.EngineHint,
            TextColor = UiTokens.Ink0,
            FontFamily = DsChrome.PixelFont,
            FontSize = 15,
            LineBreakMode = LineBreakMode.TailTruncation,
            VerticalOptions = LayoutOptions.Center,
        };
        var titleRow = new Grid
        {
            ColumnSpacing = 12,
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)],
            Children = { title, _dexProgress },
        };
        Grid.SetColumn(_dexProgress, 1);
        var hints = Kit.WindowHints(
            ("B", AppStrings.Back, CloseDexOverlay),
            ("L", AppStrings.MuseumPrevious, () => grid.JumpSection(-1)),
            ("R", AppStrings.MuseumNext, () => grid.JumpSection(1)));
        var content = new Grid
        {
            RowSpacing = 6,
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto)],
            Children = { titleRow, grid, _dexCursorInfo, hints },
        };
        Grid.SetRow(grid, 1);
        Grid.SetRow(_dexCursorInfo, 2);
        Grid.SetRow(hints, 3);

        grid.Show(ids, id =>
        {
            var state = game.Entries[id];
            return new DexGridView.Look(state.Seen, state.Caught, ShinyMark: game.ShinySpecies.Contains(id));
        });
        grid.CursorChanged += index => UpdateDexCursor(game, ids, index);
        grid.Tapped += index => grid.SetCursor(index);

        var window = Kit.DevicePanel(content, padding: 10);
        window.Margin = new Thickness(24, 12);
        var scrim = new BoxView { Color = UiTokens.Scrim };
        var scrimTap = new TapGestureRecognizer();
        scrimTap.Tapped += (_, _) => CloseDexOverlay();
        scrim.GestureRecognizers.Add(scrimTap);
        _dexOverlay = new Grid { Children = { scrim, window } };
        _hostGrid.Add(_dexOverlay);
        Grid.SetRowSpan(_dexOverlay, Math.Max(1, _hostGrid.RowDefinitions.Count));
        Grid.SetColumnSpan(_dexOverlay, Math.Max(1, _hostGrid.ColumnDefinitions.Count));
        _dexClaim = _secondScreen.Routes.OpenOverlay(SecondScreenOwner.Pokedex);
        Kit.AnimateIn(window);
        grid.SetCursor(0);
    }

    private void UpdateDexCursor(MuseumGame game, IReadOnlyList<int> ids, int index)
    {
        if ((uint)index >= (uint)ids.Count) return;
        var species = ids[index];
        var state = game.Entries[species];
        var shiny = game.ShinySpecies.Contains(species);
        if (_dexProgress is not null)
            _dexProgress.Text = AppStrings.MuseumProgress(game.Seen, game.Caught, game.Total, game.ShinyCount);
        if (_dexCursorInfo is not null)
            _dexCursorInfo.Text = $"#{species:000} {_data.SpeciesNames[species]} · {AppStrings.MuseumSpeciesState(state.Seen, state.Caught, shiny)}";
        _secondScreen.PreviewSpecies = species;
    }

    private void CloseDexOverlay()
    {
        _dexClaim?.Release();
        _dexClaim = null;
        _secondScreen.PreviewSpecies = null;
        if (_dexOverlay is not null) _hostGrid.Remove(_dexOverlay);
        _dexOverlay = null;
        _dexGrid = null;
        _dexProgress = null;
        _dexCursorInfo = null;
    }

    public bool OnPadButton(PadButton button)
    {
        if (_dexGrid is { } grid)
        {
            switch (button)
            {
                case PadButton.Left: grid.Move(-1, 0); return true;
                case PadButton.Right: grid.Move(1, 0); return true;
                case PadButton.Up: grid.Move(0, -1); return true;
                case PadButton.Down: grid.Move(0, 1); return true;
                case PadButton.L: grid.JumpSection(-1); return true;
                case PadButton.R: grid.JumpSection(1); return true;
                case PadButton.B: CloseDexOverlay(); return true;
                default: return true;
            }
        }

        switch (button)
        {
            case PadButton.Up: SelectRow(_selectedRow - 1); return true;
            case PadButton.Down: SelectRow(_selectedRow + 1); return true;
            case PadButton.A: OpenSelectedDex(); return true;
            case PadButton.B: _ = Navigation.PopAsync(); return true;
            case PadButton.X: _ = ScanAsync(); return true;
            default: return true;
        }
    }
}
