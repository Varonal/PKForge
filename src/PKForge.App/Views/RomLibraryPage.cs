using Microsoft.Maui.Controls.Shapes;
using PKForge.App.Services;
using PKForge.App.Theme;
using PKForge.App.ViewModels;
using PKForge.Domain;

namespace PKForge.App.Views;

/// <summary>
/// The game library, PlayHub-style: one card per discovered Pokémon ROM with its
/// artwork (cartridge mark as fallback), the generation · emulator subtitle, and a
/// ROM filename. Tapping a playable row launches it in the console's bound
/// emulator. Search filters by game, ROM filename, or console; the same list can
/// be navigated with a controller.
/// </summary>
public sealed class RomLibraryPage : ContentPage, IPadHandler
{
    private sealed record LibraryCard(string GameName, int Generation, LibraryRom? Rom);
    private sealed record LibraryRow(LibraryCard Card, View View);

    private readonly IRomScanner _scanner;
    private readonly IRomLibraryStore _library;
    private readonly IConsoleEmulatorStore _consoleEmulators;
    private readonly IEmulatorLauncher _launcher;
    private VerticalStackLayout _rows = null!;
    private ScrollView _scroll = null!;
    private Entry _search = null!;
    private Button _scanButton = null!;
    private Grid _hostGrid = null!;
    private Label _readout = null!;
    private Label _countLabel = null!;
    private readonly List<LibraryRow> _catalogRows = [];
    private readonly List<LibraryRow> _visibleRows = [];
    private readonly List<LibraryRow> _visiblePlayableRows = [];
    private IReadOnlyList<LibraryRom> _allRoms = [];
    private int _selectedRomIndex = -1;
    private bool _scanInProgress;

    public RomLibraryPage(IRomScanner scanner, IRomLibraryStore library, IConsoleEmulatorStore consoleEmulators, IEmulatorLauncher launcher)
    {
        _scanner = scanner;
        _library = library;
        _consoleEmulators = consoleEmulators;
        _launcher = launcher;
        Title = AppStrings.LibraryTitle;
        BackgroundColor = UiTokens.Housing;
        NavigationPage.SetHasNavigationBar(this, false);

        var back = Kit.MiniCapsule(AppStrings.Back, UiTokens.Ink0);
        back.WidthRequest = 72;
        back.Clicked += async (_, _) => await Navigation.PopAsync();

        var header = Kit.HeaderBar(AppStrings.LibraryTitle);
        header.VerticalOptions = LayoutOptions.Center;

        _countLabel = new Label
        {
            FontFamily = DsChrome.PixelFont,
            FontSize = UiTokens.TextLabel,
            TextColor = UiTokens.Ink1,
            VerticalOptions = LayoutOptions.Center,
        };

        _scanButton = Kit.Capsule(AppStrings.ScanRoms, UiTokens.Cyan);
        _scanButton.Clicked += (_, _) => _ = ScanAsync();

        var titleRow = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto)],
            Children = { back, header, _countLabel, _scanButton },
        };
        Grid.SetColumn(header, 1);
        Grid.SetColumn(_countLabel, 2);
        Grid.SetColumn(_scanButton, 3);

        _readout = new Label
        {
            TextColor = UiTokens.InkSoft,
            FontFamily = DsChrome.PixelFont,
            FontSize = UiTokens.TextBody,
            LineBreakMode = LineBreakMode.WordWrap,
        };

        _search = new Entry
        {
            Placeholder = AppStrings.LibrarySearchPlaceholder,
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing,
            ReturnType = ReturnType.Search,
            IsSpellCheckEnabled = false,
            IsTextPredictionEnabled = false,
            FontSize = 14,
            TextColor = UiTokens.Ink0,
            PlaceholderColor = UiTokens.Ink1,
            BackgroundColor = UiTokens.ShellPress,
            HeightRequest = 40,
        };
        _search.TextChanged += (_, _) => ApplySearch();
        _search.Completed += (_, _) => _search.Unfocus();

        _rows = new VerticalStackLayout { Spacing = 6 };
        _scroll = new ScrollView
        {
            Orientation = ScrollOrientation.Vertical,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = _rows,
        };
        var hints = Kit.WindowHints(
            ("A", AppStrings.Play, LaunchSelected),
            ("B", AppStrings.Back, () => _ = Navigation.PopAsync()),
            ("Y", AppStrings.LibrarySearch, () => _search.Focus()),
            ("X", AppStrings.LibraryClearSearch, () => _search.Text = string.Empty),
            ("+", AppStrings.ScanRoms, () => _ = ScanAsync()));

        var body = new Grid
        {
            RowSpacing = 10,
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)],
            Children = { Kit.Well(_readout, padding: 8), _search, _scroll, hints },
        };
        Grid.SetRow(_search, 1);
        Grid.SetRow(_scroll, 2);
        Grid.SetRow(hints, 3);

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
        Rebuild(_library.GetAll());
        IPlatformApplication.Current?.Services.GetService<GamepadRouter>()?.Push(this);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _search.Unfocus();
        IPlatformApplication.Current?.Services.GetService<GamepadRouter>()?.Remove(this);
    }

    private void Rebuild(IReadOnlyList<LibraryRom> roms)
    {
        _rows.Children.Clear();
        _catalogRows.Clear();
        _allRoms = roms;

        // Keep every ROM for an edition; selecting only the first dump hid alternate
        // copies and formats from both touch and controller users.
        var byName = roms.GroupBy(rom => Normalise(rom.GameName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key,
                group => group.OrderBy(rom => rom.FileName, StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        foreach (var (label, generation, _) in TrophyCatalog.Games)
        {
            if (byName.TryGetValue(Normalise(label), out var matches))
            {
                foreach (var rom in matches)
                    AddCatalogRow(new LibraryCard(label, generation, rom), BuildRomCard(rom));
            }
            else
            {
                AddCatalogRow(new LibraryCard(label, generation, null), BuildMissingCard(label, generation));
            }
        }
        ApplySearch();
    }

    private void AddCatalogRow(LibraryCard card, View view)
    {
        var row = new LibraryRow(card, view);
        _catalogRows.Add(row);
        _rows.Children.Add(view);
    }

    private void ApplySearch()
    {
        var selectedPath = SelectedRom()?.FilePath;
        var query = _search.Text?.Trim() ?? string.Empty;
        _visibleRows.Clear();
        _visiblePlayableRows.Clear();

        foreach (var row in _catalogRows)
        {
            var visible = Matches(row.Card, query);
            row.View.IsVisible = visible;
            if (!visible) continue;
            _visibleRows.Add(row);
            if (row.Card.Rom is not null) _visiblePlayableRows.Add(row);
        }

        _countLabel.Text = AppStrings.LibraryResults(
            _visibleRows.Count, _visibleRows.Count(row => row.Card.Rom is not null));
        _readout.Text = query.Length > 0 && _visibleRows.Count == 0
            ? AppStrings.LibraryNoResults(query)
            : _allRoms.Count > 0
                ? AppStrings.LibraryReady
                : _scanner.RomRoot is null ? AppStrings.NoRomRoot : AppStrings.LibraryScanHint;

        _selectedRomIndex = selectedPath is null
            ? -1
            : _visiblePlayableRows.FindIndex(row => row.Card.Rom?.FilePath == selectedPath);
        if (_selectedRomIndex < 0 && _visiblePlayableRows.Count > 0) _selectedRomIndex = 0;
        UpdateRomSelection(scrollIntoView: false);
    }

    private static bool Matches(LibraryCard card, string query)
    {
        if (query.Length == 0 || card.GameName.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
        if (card.Rom is not { } rom) return false;
        return rom.FileName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || ConsoleNameConverter.ConsoleLabel(rom.Console).Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private LibraryRom? SelectedRom() =>
        (uint)_selectedRomIndex < (uint)_visiblePlayableRows.Count
            ? _visiblePlayableRows[_selectedRomIndex].Card.Rom
            : null;

    private void SelectRom(int index, bool scrollIntoView = true)
    {
        if (_visiblePlayableRows.Count == 0)
        {
            _selectedRomIndex = -1;
            return;
        }
        _selectedRomIndex = Math.Clamp(index, 0, _visiblePlayableRows.Count - 1);
        UpdateRomSelection(scrollIntoView);
    }

    private void UpdateRomSelection(bool scrollIntoView)
    {
        var selectedRow = (uint)_selectedRomIndex < (uint)_visiblePlayableRows.Count
            ? _visiblePlayableRows[_selectedRomIndex]
            : null;
        foreach (var row in _catalogRows)
        {
            if (row.Card.Rom is null || row.View is not Border border) continue;
            var selected = ReferenceEquals(row, selectedRow);
            border.BackgroundColor = selected ? UiTokens.SelectFill : UiTokens.Paper;
            border.Stroke = selected ? UiTokens.Rim : UiTokens.ShellEdge;
            border.StrokeThickness = selected ? UiTokens.PanelEdge : 1;
        }
        if (scrollIntoView && selectedRow is not null)
            _ = _scroll.ScrollToAsync(selectedRow.View, ScrollToPosition.MakeVisible, true);
    }

    private void LaunchSelected()
    {
        if (SelectedRom() is { } rom) _ = LaunchAsync(rom);
    }

    private static string Normalise(string label) =>
        label.Replace("Pokémon ", "", StringComparison.OrdinalIgnoreCase)
             .Replace("Pokemon ", "", StringComparison.OrdinalIgnoreCase)
             .Replace(" Version", "", StringComparison.OrdinalIgnoreCase).Trim();

    /// <summary>A catalog game the player has no ROM of: muted card, missing label.</summary>
    private View BuildMissingCard(string label, int generation)
    {
        // The artwork loads for missing games too: the catalog always shows its face.
        var art = new Image
        {
            Aspect = Aspect.AspectFit,
            WidthRequest = 44,
            HeightRequest = 44,
            IsVisible = false,
        };
        var mark = new GameCartridgeMark { WidthRequest = 40, HeightRequest = 40, Generation = generation };
        _ = LoadArtAsync(art, mark, Normalise(label));
        var title = new Label
        {
            Text = Normalise(label),
            FontFamily = DsChrome.PixelFont,
            FontSize = 16,
            TextColor = UiTokens.Ink1,
            VerticalTextAlignment = TextAlignment.Center,
        };
        var subtitle = new Label
        {
            Text = $"Gen {generation}",
            FontFamily = DsChrome.PixelFont,
            FontSize = UiTokens.TextLabel,
            TextColor = UiTokens.InkSoft,
            VerticalTextAlignment = TextAlignment.Center,
        };
        var missing = new Label
        {
            Text = AppStrings.Fr ? "Pas de ROM" : "No ROM",
            FontFamily = DsChrome.PixelFont,
            FontSize = UiTokens.TextLabel,
            TextColor = UiTokens.Ink1,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalOptions = LayoutOptions.End,
        };
        var text = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center, Children = { title, subtitle } };
        var card = new Border
        {
            BackgroundColor = UiTokens.Housing,
            Stroke = UiTokens.ShellEdge,
            StrokeThickness = 1,
            Opacity = 0.6,
            StrokeShape = new RoundRectangle { CornerRadius = UiTokens.ControlRadius },
            Padding = new Thickness(10, 8),
            Content = new Grid
            {
                ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)],
                ColumnSpacing = 12,
                Children = { new Grid { Children = { mark, art } }, text, missing },
            },
        };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(missing, 2);
        return card;
    }

    /// <summary>One playable ROM card, including its source filename and console.</summary>
    private View BuildRomCard(LibraryRom rom)
    {
        // The artwork loads asynchronously at build time: the cartridge mark shows
        // until the icon is ready, then takes its place (PlayHub's pattern).
        var art = new Image
        {
            Aspect = Aspect.AspectFit,
            WidthRequest = 44,
            HeightRequest = 44,
            IsVisible = false,
        };
        var mark = new GameCartridgeMark { WidthRequest = 40, HeightRequest = 40, Generation = rom.Generation };
        _ = LoadArtAsync(art, mark, rom.GameName);

        var title = new Label
        {
            FontFamily = DsChrome.PixelFont,
            FontSize = 16,
            TextColor = UiTokens.Ink0,
            VerticalTextAlignment = TextAlignment.Center,
        };
        title.SetBinding(Label.TextProperty, "GameName");

        var subtitle = new Label
        {
            FontFamily = DsChrome.PixelFont,
            FontSize = UiTokens.TextLabel,
            TextColor = UiTokens.Ink1,
            VerticalTextAlignment = TextAlignment.Center,
        };
        subtitle.SetBinding(Label.TextProperty, new Binding("Console", converter: ConsoleNameConverter.Instance));

        var status = new Label
        {
            FontFamily = DsChrome.PixelFont,
            FontSize = UiTokens.TextLabel,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalOptions = LayoutOptions.End,
            TextColor = UiTokens.Cyan,
        };
        status.SetBinding(Label.TextProperty, new Binding("FileName", converter: FileNameTruncateConverter.Instance));

        var text = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children = { title, subtitle },
        };

        var row = new Border
        {
            BackgroundColor = UiTokens.Paper,
            Stroke = UiTokens.ShellEdge,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = UiTokens.ControlRadius },
            Padding = new Thickness(10, 8),
            Content = new Grid
            {
                ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)],
                ColumnSpacing = 12,
                Children = { new Grid { Children = { mark, art } }, text, status },
            },
        };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(status, 2);

        // Every binding in the card reads from the row's own context.
        row.BindingContext = rom;
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            var index = _visiblePlayableRows.FindIndex(candidate =>
                string.Equals(candidate.Card.Rom?.FilePath, rom.FilePath, StringComparison.Ordinal));
            if (index >= 0) SelectRom(index, scrollIntoView: false);
            _ = LaunchAsync(rom);
        };
        row.GestureRecognizers.Add(tap);
        return row;
    }

    /// <summary>Loads the game's artwork; the cartridge mark stays until it lands.</summary>
    private static async Task LoadArtAsync(Image art, GameCartridgeMark mark, string gameName)
    {
        var path = await GameArt.GetIconAsync($"Pokémon {gameName}");
        if (path is null) return;
        art.Source = path;
        art.IsVisible = true;
        mark.IsVisible = false;
    }

    /// <summary>Launches ROMs with an integrated core in PKForge; otherwise uses the
    /// selected console emulator. Pokémon ROMs never follow a stale external DS binding.</summary>
    private async Task LaunchAsync(LibraryRom rom)
    {
        var bound = _consoleEmulators.Find(rom.Console);
        var generation = RomNameRules.GenerationOf(rom.GameName);
        if (ConsoleRules.RunsInApp(rom.Console))
        {
            var services = IPlatformApplication.Current?.Services;
            var romLinks = services?.GetService<IRomLinkStore>();
            var linkedSave = romLinks?.GetAll()
                .FirstOrDefault(link => string.Equals(link.RomUri, rom.FilePath, StringComparison.Ordinal));
            if (linkedSave is not null && RomLinkRules.UsesIntegratedCore(generation)
                && linkedSave.Emulator != EmulatorKind.Embedded)
            {
                romLinks?.Set(linkedSave with { Emulator = EmulatorKind.Embedded, Generation = generation });
            }
            var saveFiles = linkedSave is null ? null : services?.GetService<ISaveFileAccess>();
            var start = await Task.Run(() => Platforms.Android.EmbeddedGameSession.TryStart(
                rom.FilePath, generation, rom.FileName, linkedSave?.SaveDocumentId, saveFiles));
            if (start.Session is { } session)
            {
                await MainThread.InvokeOnMainThreadAsync(() => Navigation.PushAsync(new EmbeddedGamePage(session)));
                return;
            }
            await PadMenu.ShowAsync(_hostGrid, AppStrings.GameLaunchFailedTitle,
                AppStrings.GameLaunchFailed(rom.GameName, start.Error, generation), AppStrings.Ok);
            return;
        }
        var fallback = ConsoleRules.EmulatorsFor(rom.Console)
            .Where(kind => kind != EmulatorKind.Embedded)
            .Where(kind => EmulatorCatalog.IsInstalled(kind, _launcher.IsInstalled))
            .Select(kind => (EmulatorKind?)kind)
            .FirstOrDefault();
        if (bound is null && fallback is null)
        {
            await PadMenu.ShowAsync(_hostGrid, AppStrings.RomMissing,
                $"{rom.GameName}: {AppStrings.NotInstalled}.", AppStrings.Ok);
            return;
        }
        var kind = bound ?? fallback!.Value;
        var link = new RomLink("library", rom.FilePath, rom.FileName, kind,
            RomNameRules.GenerationOf(rom.GameName), $"Pokémon {rom.GameName}");
        if (await _launcher.LaunchAsync(link))
        {
            // The game runs: the lower screen becomes its companion until the
            // player comes back.
            await (IPlatformApplication.Current?.Services.GetService<Services.GameCompanionStore>()?.StartForGameAsync(link) ?? Task.CompletedTask);
            return;
        }
        await PadMenu.ShowAsync(_hostGrid, AppStrings.RomMissing,
            $"{SaveDescriptions.EmulatorName(kind)}: {rom.FileName}", AppStrings.Ok);
    }

    /// <summary>One pass over every console's ES-DE folder; the library is replaced wholesale.</summary>
    private async Task ScanAsync()
    {
        if (_scanInProgress) return;
        _scanInProgress = true;
        _scanButton.IsEnabled = false;
        _readout.Text = AppStrings.Scanning;
        try
        {
            var roms = await Task.Run(() =>
            {
                var found = new List<LibraryRom>();
                foreach (var console in Enum.GetValues<GameConsole>())
                    found.AddRange(_scanner.Scan(console));
                return found;
            }).ConfigureAwait(true);
            _library.ReplaceAll(roms);
            Rebuild(roms);
        }
        catch (Exception error)
        {
            _readout.Text = error.Message;
        }
        finally
        {
            _scanInProgress = false;
            _scanButton.IsEnabled = true;
        }
    }

    public bool OnPadButton(PadButton button)
    {
        switch (button)
        {
            case PadButton.Up: SelectRom(_selectedRomIndex - 1); return true;
            case PadButton.Down: SelectRom(_selectedRomIndex + 1); return true;
            case PadButton.A: LaunchSelected(); return true;
            case PadButton.B: _ = Navigation.PopAsync(); return true;
            case PadButton.X: _search.Text = string.Empty; return true;
            case PadButton.Y: _search.Focus(); return true;
            case PadButton.Start: _ = ScanAsync(); return true;
            default: return true; // own the pad while this page is up
        }
    }
}

/// <summary>Console enum → display name.</summary>
public sealed class ConsoleNameConverter : IValueConverter
{
    public static readonly ConsoleNameConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        value is GameConsole console ? ConsoleLabel(console) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();

    public static string ConsoleLabel(GameConsole console) => console switch
    {
        GameConsole.GameBoy => "Game Boy",
        GameConsole.GameBoyColor => "Game Boy Color",
        GameConsole.GameBoyAdvance => "Game Boy Advance",
        GameConsole.NintendoDs => "Nintendo DS",
        GameConsole.Nintendo3Ds => "Nintendo 3DS",
        GameConsole.GameCube => "GameCube",
        GameConsole.NintendoSwitch => "Switch",
        _ => console.ToString(),
    };
}

/// <summary>File names get one truncated line.</summary>
public sealed class FileNameTruncateConverter : IValueConverter
{
    public static readonly FileNameTruncateConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not string name) return string.Empty;
        return name.Length > 28 ? name[..25] + "…" : name;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}
