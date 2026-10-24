using PKForge.App.Services;
using PKForge.App.Theme;
using PKForge.App.ViewModels;
using PKForge.Domain;

namespace PKForge.App.Views;

/// <summary>
/// The ROM library: one scan over the ES-DE tree (/storage/emulated/0/ROMs) finds every
/// Pokémon ROM, and a tap launches it in the console's bound emulator (the Cocoon-style
/// binding from the Emulators tab). No save, no folder picker: the console binding and
/// the file path are all a launch needs.
/// </summary>
public sealed class RomLibraryPage : ContentPage, IPadHandler
{
    private readonly IRomScanner _scanner;
    private readonly IRomLibraryStore _library;
    private readonly IConsoleEmulatorStore _consoleEmulators;
    private readonly IEmulatorLauncher _launcher;
    private readonly List<LibraryRom> _roms = [];
    private VerticalStackLayout _rows = null!;
    private Grid _hostGrid = null!;
    private Label _readout = null!;

    public RomLibraryPage(IRomScanner scanner, IRomLibraryStore library, IConsoleEmulatorStore consoleEmulators, IEmulatorLauncher launcher)
    {
        _scanner = scanner;
        _library = library;
        _consoleEmulators = consoleEmulators;
        _launcher = launcher;
        Title = AppStrings.LibraryTitle;
        BackgroundColor = UiTokens.Housing;
        NavigationPage.SetHasNavigationBar(this, false);

        var back = Kit.MiniCapsule("Back", UiTokens.Ink0);
        back.WidthRequest = 72;
        back.Clicked += async (_, _) => await Navigation.PopAsync();

        var header = Kit.HeaderBar(AppStrings.LibraryTitle);
        header.VerticalOptions = LayoutOptions.Center;

        var scan = Kit.Capsule(AppStrings.ScanRoms, UiTokens.Cyan);
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
        foreach (var rom in roms.OrderBy(r => r.Console).ThenBy(r => r.GameName, StringComparer.OrdinalIgnoreCase))
        {
            _roms.Add(rom);
            _rows.Children.Add(BuildRomRow(rom));
        }
        _readout.Text = roms.Count > 0
            ? AppStrings.RomCount(roms.Count)
            : _scanner.RomRoot is null ? AppStrings.NoRomRoot : AppStrings.ScanRoms + " ?";
    }

    private View BuildRomRow(LibraryRom rom)
    {
        var game = new Label
        {
            Text = rom.GameName,
            TextColor = UiTokens.Ink0,
            FontFamily = DsChrome.PixelFont,
            FontSize = 14,
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
        };
        var detail = new Label
        {
            Text = $"{rom.SystemFolder} · {rom.FileName}",
            TextColor = UiTokens.InkSoft,
            FontFamily = DsChrome.PixelFont,
            FontSize = 10,
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
        };
        var identity = new Grid
        {
            RowSpacing = 2,
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto)],
            Children = { game, detail },
        };
        Grid.SetRow(detail, 1);
        var row = new Grid
        {
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)],
            ColumnSpacing = 8,
            Children =
            {
                identity,
                new Label
                {
                    Text = AppStrings.Play, TextColor = UiTokens.Cyan,
                    FontFamily = DsChrome.PixelFont, FontSize = 12, VerticalOptions = LayoutOptions.Center,
                },
            },
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => _ = LaunchAsync(rom);
        row.GestureRecognizers.Add(tap);
        return Kit.Well(row, padding: 8);
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
                $"{rom.GameName}: {AppStrings.NotInstalled}.", "OK");
            return;
        }
        var kind = bound ?? fallback;
        var link = new RomLink("library", rom.FilePath, rom.FileName, kind,
            RomNameRules.GenerationOf(rom.GameName), rom.GameName);
        if (!await _launcher.LaunchAsync(link))
            await PadMenu.ShowAsync(_hostGrid, AppStrings.RomMissing,
                $"{SaveDescriptions.EmulatorName(kind)}: {rom.FileName}", "OK");
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
