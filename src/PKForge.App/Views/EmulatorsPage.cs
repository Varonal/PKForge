using CommunityToolkit.Mvvm.Input;
using PKForge.App.Theme;
using PKForge.App.ViewModels;
using PKForge.Domain;

namespace PKForge.App.Views;

/// <summary>
/// The Emulators tab: one chip per emulator with its install and link state, the actions
/// the home Link menu already knows (folder grant + scan), and the launcher — a chip tap
/// links, a linked chip with an installed emulator opens it. L/R moves between the chips.
/// </summary>
public sealed class EmulatorsPage : ContentPage, IPadHandler
{
    private readonly SavePickerViewModel _viewModel;
    private readonly IEmulatorLauncher _launcher;
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

    public EmulatorsPage(SavePickerViewModel viewModel, IEmulatorLauncher launcher)
    {
        _viewModel = viewModel;
        _launcher = launcher;
        BindingContext = viewModel;
        Title = "Emulators";
        BackgroundColor = UiTokens.Housing;
        NavigationPage.SetHasNavigationBar(this, false);

        var back = Kit.MiniCapsule("Back", UiTokens.Ink0);
        back.WidthRequest = 72;
        back.Clicked += async (_, _) => await Navigation.PopAsync();

        var header = Kit.HeaderBar("Emulators");
        header.VerticalOptions = LayoutOptions.Center;

        var linkAll = Kit.Capsule("Link all installed", UiTokens.Cyan);
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
        public string State => Linked ? "Linked" : Installed ? "Not linked" : "Not installed";
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
        // The chips come from the catalogue, the states from PackageManager and the grants.
        var roots = IPlatformApplication.Current!.Services.GetRequiredService<IWatchedRootStore>().GetRoots();
        _chips.Clear();
        _rows.Children.Clear();
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
            _viewModel.Status = "Every installed emulator is already linked.";
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
            _viewModel.Status = $"{SaveDescriptions.EmulatorName(kind)} cannot be linked yet.";
            return;
        }
        await command.ExecuteAsync(null);
        RefreshChips();
    }

    private bool MoveCursor(int delta)
    {
        if (_chips.Count == 0) return true;
        var previous = _index >= 0 && _index < _rows.Children.Count ? _rows.Children[_index] as VisualElement : null;
        _index = Math.Clamp(_index < 0 ? 0 : _index + delta, 0, _chips.Count - 1);
        _ = previous?.ScaleTo(1.0, 80);
        if (_rows.Children[_index] is VisualElement current)
        {
            _ = current.ScaleTo(1.03, 90);
            _ = current.ScaleTo(1.0, 80);
        }
        return true;
    }
}
