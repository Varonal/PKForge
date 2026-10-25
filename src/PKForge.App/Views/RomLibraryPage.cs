using System.Collections.ObjectModel;
using Microsoft.Maui.Controls.Shapes;
using PKForge.App.Services;
using PKForge.App.Theme;
using PKForge.App.ViewModels;
using PKForge.Domain;

namespace PKForge.App.Views;

/// <summary>
/// The game library, PlayHub-style: one card per discovered Pokémon ROM with its
/// artwork (cartridge mark as fallback), the generation · emulator subtitle, and a
/// colored status line — green when a save was found, amber when the ROM is linked,
/// muted otherwise. Tapping a playable row launches it in the console's bound
/// emulator. The emulator chips above link in one tap through the standard save
/// folders, never a folder picker.
/// </summary>
public sealed class RomLibraryPage : ContentPage, IPadHandler
{
    private readonly IRomScanner _scanner;
    private readonly IRomLibraryStore _library;
    private readonly IConsoleEmulatorStore _consoleEmulators;
    private readonly IEmulatorLauncher _launcher;
    private readonly DirectSaveScanner? _saveScanner;
    private readonly ObservableCollection<LibraryRom> _roms = [];
    private VerticalStackLayout _rows = null!;
    private Grid _hostGrid = null!;
    private Label _readout = null!;
    private Label _countLabel = null!;

    public RomLibraryPage(IRomScanner scanner, IRomLibraryStore library, IConsoleEmulatorStore consoleEmulators, IEmulatorLauncher launcher, ISaveEngine? engine = null)
    {
        _scanner = scanner;
        _library = library;
        _consoleEmulators = consoleEmulators;
        _launcher = launcher;
        _saveScanner = engine is not null ? new DirectSaveScanner(engine) : null;
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

        var scan = Kit.Capsule(AppStrings.ScanRoms, UiTokens.Cyan);
        scan.Clicked += (_, _) => _ = ScanAsync();

        var titleRow = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto)],
            Children = { back, header, _countLabel, scan },
        };
        Grid.SetColumn(header, 1);
        Grid.SetColumn(_countLabel, 2);
        Grid.SetColumn(scan, 3);

        _readout = new Label
        {
            TextColor = UiTokens.InkSoft,
            FontFamily = DsChrome.PixelFont,
            FontSize = UiTokens.TextBody,
            LineBreakMode = LineBreakMode.WordWrap,
        };

        _rows = new VerticalStackLayout { Spacing = 6 };
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

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Rebuild(_library.GetAll());
        IPlatformApplication.Current?.Services.GetService<GamepadRouter>()?.Push(this);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        IPlatformApplication.Current?.Services.GetService<GamepadRouter>()?.Remove(this);
    }

    private void Rebuild(IReadOnlyList<LibraryRom> roms)
    {
        _roms.Clear();
        _rows.Children.Clear();
        var ordered = roms.OrderBy(r => r.Console).ThenBy(r => r.GameName, StringComparer.OrdinalIgnoreCase);
        foreach (var rom in ordered)
        {
            _roms.Add(rom);
            _rows.Children.Add(BuildRomCard(rom));
        }
        _countLabel.Text = AppStrings.RomCount(roms.Count);
        _readout.Text = roms.Count > 0
            ? AppStrings.RomCount(roms.Count)
            : _scanner.RomRoot is null ? AppStrings.NoRomRoot : AppStrings.Fr
                ? "Appuyez sur Scanner pour chercher vos ROMs dans /storage/emulated/0/ROMs."
                : "Tap Scan to look for your ROMs in /storage/emulated/0/ROMs.";
    }

    /// <summary>One game card: artwork (cartridge mark as fallback), title, generation·
    /// emulator subtitle, and the colored status line.</summary>
    private View BuildRomCard(LibraryRom rom)
    {
        var art = new Image
        {
            Aspect = Aspect.AspectFit,
            WidthRequest = 44,
            HeightRequest = 44,
            IsVisible = false,
        };
        var mark = new GameCartridgeMark { WidthRequest = 40, HeightRequest = 40 };
        art.BindingContextChanged += async (_, _) =>
        {
            if (art.BindingContext is not LibraryRom card) return;
            art.IsVisible = false;
            mark.IsVisible = true;
            var path = await GameArt.GetIconAsync($"Pokémon {card.GameName}");
            if (!ReferenceEquals(art.BindingContext, card)) return;
            art.Source = path;
            art.IsVisible = path is not null;
            mark.IsVisible = path is null;
        };
        art.BindingContext = rom;
        mark.BindingContext = rom;
        mark.SetBinding(GameCartridgeMark.GenerationProperty, "Generation");

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

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => _ = LaunchAsync(rom);
        row.GestureRecognizers.Add(tap);
        return row;
    }

    /// <summary>A tap launches: the console's bound emulator, and the ROM's plain path.</summary>
    private async Task LaunchAsync(LibraryRom rom)
    {
        var bound = _consoleEmulators.Find(rom.Console);
        var fallback = ConsoleRules.EmulatorsFor(rom.Console)
            .FirstOrDefault(kind => EmulatorCatalog.IsInstalled(kind, _launcher.IsInstalled));
        if (bound is null && fallback == default)
        {
            await PadMenu.ShowAsync(_hostGrid, AppStrings.RomMissing,
                $"{rom.GameName}: {AppStrings.NotInstalled}.", AppStrings.Ok);
            return;
        }
        var kind = bound ?? fallback;
        var link = new RomLink("library", rom.FilePath, rom.FileName, kind,
            RomNameRules.GenerationOf(rom.GameName), $"Pokémon {rom.GameName}");
        if (!await _launcher.LaunchAsync(link))
            await PadMenu.ShowAsync(_hostGrid, AppStrings.RomMissing,
                $"{SaveDescriptions.EmulatorName(kind)}: {rom.FileName}", AppStrings.Ok);
    }

    /// <summary>One pass over every console's ES-DE folder; the library is replaced wholesale.</summary>
    private async Task ScanAsync()
    {
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

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        value is string name && name.Length > 28 ? name[..25] + "…" : name ?? string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}
