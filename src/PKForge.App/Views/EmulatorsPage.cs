using CommunityToolkit.Mvvm.Input;
using PKForge.App.Services;
using PKForge.App.Theme;
using PKForge.App.ViewModels;
using PKForge.Domain;

namespace PKForge.App.Views;

/// <summary>
/// The Emulators tab, Cocoon-style: one section per console with the emulator that
/// launches its ROMs (tap to choose among the installed ones), then the per-emulator
/// save-folder bindings below. A console bound to an emulator is all the library needs
/// to launch a ROM — no save required.
/// </summary>
public sealed class EmulatorsPage : ContentPage, IPadHandler
{
    private readonly SavePickerViewModel _viewModel;
    private readonly IEmulatorLauncher _launcher;
    private readonly IConsoleEmulatorStore _consoleEmulators;
    private readonly List<EmulatorChip> _chips = [];
    private VerticalStackLayout _rows = null!;
    private Grid _hostGrid = null!;
    private int _index = -1;

    /// <summary>The folder-grant command per emulator, the same ones Home's Link menu runs.</summary>
    private IAsyncRelayCommand? AddCommand(EmulatorKind kind) => kind switch
    {
        EmulatorKind.RetroArch => _viewModel.AddRetroArchCommand,
        EmulatorKind.MelonDS => _viewModel.AddMelonDsCommand,
        EmulatorKind.Azahar => _viewModel.AddAzaharCommand,
        EmulatorKind.CitraMmj => _viewModel.AddCitraMmjCommand,
        EmulatorKind.Eden => _viewModel.AddEdenCommand,
        EmulatorKind.Dolphin => _viewModel.AddDolphinCommand,
        EmulatorKind.DraStic => _viewModel.AddDraSticCommand,
        EmulatorKind.Linkboy => _viewModel.AddLinkboyCommand,
        EmulatorKind.PizzaBoyGba => _viewModel.AddPizzaBoyGbaCommand,
        EmulatorKind.PizzaBoyGbc => _viewModel.AddPizzaBoyGbcCommand,
        _ => null,
    };

    public EmulatorsPage(SavePickerViewModel viewModel, IEmulatorLauncher launcher, IConsoleEmulatorStore consoleEmulators)
    {
        _viewModel = viewModel;
        _launcher = launcher;
        _consoleEmulators = consoleEmulators;
        BindingContext = viewModel;
        Title = AppStrings.EmulatorsTitle;
        BackgroundColor = UiTokens.Housing;
        NavigationPage.SetHasNavigationBar(this, false);

        var back = Kit.MiniCapsule("Back", UiTokens.Ink0);
        back.WidthRequest = 72;
        back.Clicked += async (_, _) => await Navigation.PopAsync();

        var header = Kit.HeaderBar(AppStrings.EmulatorsTitle);
        header.VerticalOptions = LayoutOptions.Center;

        var linkAll = Kit.Capsule(AppStrings.LinkAllInstalled, UiTokens.Cyan);
        linkAll.Clicked += (_, _) => _ = LinkAllInstalledAsync();

        var titleRow = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)],
            Children = { back, header, linkAll },
        };
        Grid.SetColumn(header, 1);
        Grid.SetColumn(linkAll, 2);

        var readout = new Label
        {
            TextColor = UiTokens.InkSoft,
            FontFamily = DsChrome.PixelFont,
            FontSize = UiTokens.TextBody,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        readout.SetBinding(Label.TextProperty, new Binding(nameof(SavePickerViewModel.Status), converter: Kit.TidyText));

        _rows = new VerticalStackLayout { Spacing = 8 };

        var scrollableRows = new ScrollView
        {
            Orientation = ScrollOrientation.Vertical,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = _rows,
        };
        var body = new Grid
        {
            RowSpacing = 10,
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Star)],
            Children = { Kit.Well(readout, padding: 8), scrollableRows },
        };
        Grid.SetRow(scrollableRows, 1);

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

    /// <summary>One emulator row: identity, link/install state, and what a tap does.</summary>
    private sealed class EmulatorChip(EmulatorKind kind, bool linked, bool installed)
    {
        public EmulatorKind Kind { get; } = kind;
        public bool Linked { get; } = linked;
        public bool Installed { get; } = installed;
        public string Name => SaveDescriptions.EmulatorName(Kind);
        public string Systems => EmulatorCatalog.SystemsOf(Kind);
        public string State => Linked ? AppStrings.Linked : Installed ? AppStrings.NotLinked : AppStrings.NotInstalled;
        public string Icon => Kind switch
        {
            EmulatorKind.RetroArch => "retroarch",
            EmulatorKind.MelonDS => "melonds",
            EmulatorKind.Linkboy => "linkboy",
            EmulatorKind.Azahar or EmulatorKind.CitraMmj => "azahar",
            EmulatorKind.Eden => "eden",
            EmulatorKind.Dolphin => "dolphin",
            EmulatorKind.DraStic => "drastic",
            EmulatorKind.PizzaBoyGba or EmulatorKind.PizzaBoyGbc => "pizzaboy",
            _ => "storage",
        };
    }

    /// <summary>One console row: its bound emulator, or "choose" when unbound.</summary>
    private sealed class ConsoleRow(GameConsole console, EmulatorKind? bound, bool anyInstalled)
    {
        public GameConsole Console { get; } = console;
        public EmulatorKind? Bound { get; set; } = bound;
        public bool AnyInstalled { get; } = anyInstalled;
        public string Name => Console switch
        {
            GameConsole.GameBoy => "Game Boy",
            GameConsole.GameBoyColor => "Game Boy Color",
            GameConsole.GameBoyAdvance => "Game Boy Advance",
            GameConsole.NintendoDs => "Nintendo DS",
            GameConsole.Nintendo3Ds => "Nintendo 3DS",
            GameConsole.GameCube => "GameCube",
            GameConsole.NintendoSwitch => "Switch",
            _ => Console.ToString(),
        };
        public string Icon => Console switch
        {
            GameConsole.GameBoy or GameConsole.GameBoyColor => "platform-gb",
            GameConsole.GameBoyAdvance => "platform-gba",
            GameConsole.NintendoDs => "platform-ds",
            GameConsole.Nintendo3Ds => "platform-ds",
            GameConsole.GameCube => "platform-gc",
            GameConsole.NintendoSwitch => "eden",
            _ => "storage",
        };
    }

    private View BuildConsoleRow(ConsoleRow row)
    {
        var icon = PksmIcons.Icon(row.Icon, 30, PksmIcons.White);
        var name = new Label
        {
            Text = row.Name,
            TextColor = UiTokens.Ink0,
            FontFamily = DsChrome.PixelFont,
            FontSize = 15,
            VerticalOptions = LayoutOptions.Center,
        };
        var binding = new Label
        {
            Text = row.Bound is { } bound
                ? $"{AppStrings.ConsoleBound}: {SaveDescriptions.EmulatorName(bound)}"
                : row.AnyInstalled ? AppStrings.BindConsole : AppStrings.NotInstalled,
            TextColor = row.Bound is null ? UiTokens.InkSoft : UiTokens.Green,
            FontFamily = DsChrome.PixelFont,
            FontSize = 12,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End,
        };
        var line = new Grid
        {
            ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)],
            ColumnSpacing = 10,
            Children = { icon, name, binding },
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => _ = ChooseConsoleEmulatorAsync(row);
        line.GestureRecognizers.Add(tap);
        return Kit.Well(line, padding: 10);
    }

    /// <summary>The Cocoon flow: the console's installed emulators, one tap binds.</summary>
    private async Task ChooseConsoleEmulatorAsync(ConsoleRow row)
    {
        var candidates = ConsoleRules.EmulatorsFor(row.Console)
            .Where(kind => EmulatorCatalog.IsInstalled(kind, _launcher.IsInstalled))
            .ToList();
        if (candidates.Count == 0)
        {
            _viewModel.Status = $"{row.Name}: {AppStrings.NotInstalled}.";
            return;
        }
        var options = new List<PadOption>();
        var current = _consoleEmulators.Find(row.Console);
        foreach (var kind in candidates)
        {
            var name = SaveDescriptions.EmulatorName(kind);
            options.Add(new PadOption(current == kind ? $"{name} ✓" : name, IconPath: EmulatorChipIcon(kind)));
        }
        options.Add(new PadOption(AppStrings.BindConsole, IconPath: "close"));
        var choice = await PadMenu.ShowAsync(_hostGrid, row.Name,
            AppStrings.BindConsole, [.. options]);
        if (choice is null || choice == AppStrings.BindConsole) return;
        var picked = candidates.FirstOrDefault(kind => choice.StartsWith(SaveDescriptions.EmulatorName(kind), StringComparison.Ordinal));
        if (picked is { } emulator)
        {
            _consoleEmulators.Set(row.Console, emulator);
            RefreshChips();
            _viewModel.Status = $"{row.Name} → {SaveDescriptions.EmulatorName(emulator)}";
        }
    }

    private static string EmulatorChipIcon(EmulatorKind kind) => kind switch
    {
        EmulatorKind.RetroArch => "retroarch",
        EmulatorKind.MelonDS => "melonds",
        EmulatorKind.Linkboy => "linkboy",
        EmulatorKind.Azahar or EmulatorKind.CitraMmj => "azahar",
        EmulatorKind.Eden => "eden",
        EmulatorKind.Dolphin => "dolphin",
        EmulatorKind.DraStic => "drastic",
        EmulatorKind.PizzaBoyGba or EmulatorKind.PizzaBoyGbc => "pizzaboy",
        _ => "storage",
    };

    private View BuildChipRow(EmulatorChip chip)
    {
        var icon = PksmIcons.Icon(chip.Icon, 30, PksmIcons.White);

        var name = new Label
        {
            Text = chip.Name,
            TextColor = UiTokens.Ink0,
            FontFamily = DsChrome.PixelFont,
            FontSize = 15,
            VerticalOptions = LayoutOptions.Center,
        };
        var systems = new Label
        {
            Text = chip.Systems,
            TextColor = UiTokens.InkSoft,
            FontFamily = DsChrome.PixelFont,
            FontSize = 11,
            VerticalOptions = LayoutOptions.Center,
        };
        var state = new Label
        {
            Text = chip.State,
            TextColor = chip.Linked ? UiTokens.Green : chip.Installed ? UiTokens.InkSoft : UiTokens.Ink1,
            FontFamily = DsChrome.PixelFont,
            FontSize = 12,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End,
        };

        var identity = new Grid
        {
            RowSpacing = 2,
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto)],
            Children = { name, systems },
        };
        Grid.SetRow(systems, 1);

        var row = new Grid
        {
            ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)],
            ColumnSpacing = 10,
            Children = { icon, identity, state },
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => _ = LinkAsync(chip.Kind);
        row.GestureRecognizers.Add(tap);
        return Kit.Well(row, padding: 10);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        RefreshChips();
        IPlatformApplication.Current?.Services.GetService<GamepadRouter>()?.Push(this);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        IPlatformApplication.Current?.Services.GetService<GamepadRouter>()?.Remove(this);
    }

    public void RefreshChips()
    {
        // Consoles first (the Cocoon binding), then the per-emulator save folders.
        var roots = IPlatformApplication.Current!.Services.GetRequiredService<IWatchedRootStore>().GetRoots();
        _chips.Clear();
        _rows.Children.Clear();
        var consoles = Enum.GetValues<GameConsole>();
        foreach (var console in consoles)
        {
            var bound = _consoleEmulators.Find(console);
            var anyInstalled = ConsoleRules.AnyInstalled(console, _launcher.IsInstalled);
            if (bound is null && !anyInstalled) continue; // a console nothing can run is not shown
            var row = new ConsoleRow(console, bound, anyInstalled);
            _rows.Children.Add(BuildConsoleRow(row));
        }
        if (_rows.Children.Count > 0)
            _rows.Children.Add(new BoxView { HeightRequest = 10, Color = UiTokens.Housing });
        foreach (var app in EmulatorCatalog.All)
        {
            var installed = EmulatorCatalog.IsInstalled(app.Kind, _launcher.IsInstalled);
            var linked = roots.Any(r => r.Kind == app.Kind);
            var chip = new EmulatorChip(app.Kind, linked, installed);
            _chips.Add(chip);
            _rows.Children.Add(BuildChipRow(chip));
        }
    }

    private async Task LinkAllInstalledAsync()
    {
        // One folder picker per installed-and-unlinked emulator, in catalogue order: the
        // same flow as Home's Link menu, without the platform detour.
        var roots = IPlatformApplication.Current!.Services.GetRequiredService<IWatchedRootStore>();
        var pending = EmulatorCatalog.All
            .Where(app => EmulatorCatalog.IsInstalled(app.Kind, _launcher.IsInstalled))
            .Where(app => roots.GetRoots().Any(r => r.Kind == app.Kind) == false)
            .ToList();
        if (pending.Count == 0)
        {
            _viewModel.Status = AppStrings.AlreadyAllLinked;
            return;
        }
        foreach (var app in pending)
        {
            var command = AddCommand(app.Kind);
            if (command is not null) await command.ExecuteAsync(null);
        }
        RefreshChips();
    }

    public bool OnPadButton(PadButton button)
    {
        switch (button)
        {
            case PadButton.Up: return MoveCursor(-1);
            case PadButton.Down: return MoveCursor(1);
            case PadButton.A: return ActivateChipAsync().GetAwaiter().GetResult();
            case PadButton.B: _ = Navigation.PopAsync(); return true;
            default: return true;
        }
    }

    private async Task<bool> ActivateChipAsync()
    {
        if (_index < 0 || _index >= _chips.Count) return true;
        var chip = _chips[_index];
        await LinkAsync(chip.Kind);
        return true;
    }

    private async Task LinkAsync(EmulatorKind kind)
    {
        var command = AddCommand(kind);
        if (command is null)
        {
            _viewModel.Status = $"{SaveDescriptions.EmulatorName(kind)}: {AppStrings.NotLinked}.";
            return;
        }
        await command.ExecuteAsync(null);
        RefreshChips();
    }

    private bool MoveCursor(int delta)
    {
        if (_rows.Children.Count == 0) return true;
        var previous = _index >= 0 && _index < _rows.Children.Count ? _rows.Children[_index] as VisualElement : null;
        _index = Math.Clamp(_index < 0 ? 0 : _index + delta, 0, _rows.Children.Count - 1);
        _ = previous?.ScaleToAsync(1.0, 80);
        if (_rows.Children[_index] is VisualElement current)
        {
            _ = current.ScaleToAsync(1.03, 90);
            _ = current.ScaleToAsync(1.0, 80);
        }
        return true;
    }
}
