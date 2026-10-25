using PKForge.App.Services;
using PKForge.App.Theme;
using PKForge.App.ViewModels;
using PKForge.Domain;

namespace PKForge.App.Views;

/// <summary>
/// The RetroAchievements screen: a sign-in panel for username and API key, then one
/// card per linked Gen 1-5 game with its official achievement set and the player's
/// unlock state. Cached sets open instantly; Refresh re-queries the RA API.
/// </summary>
public sealed class RetroAchievementsPage : ContentPage, IPadHandler
{
    private readonly RetroAchievementsViewModel _viewModel;
    private readonly SavePickerViewModel _saves;
    private Grid _hostGrid = null!;
    private CollectionView _list = null!;
    private View _signInPanel = null!;
    private int _index = -1;

    public RetroAchievementsPage(RetroAchievementsViewModel viewModel, SavePickerViewModel saves)
    {
        _viewModel = viewModel;
        _saves = saves;
        BindingContext = viewModel;
        Title = "RetroAchievements";
        BackgroundColor = UiTokens.Housing;
        NavigationPage.SetHasNavigationBar(this, false);

        var back = Kit.MiniCapsule(AppStrings.Back, UiTokens.Ink0);
        back.WidthRequest = 72;
        back.Clicked += async (_, _) => await Navigation.PopAsync();

        var header = Kit.HeaderBar("RetroAchievements");
        header.VerticalOptions = LayoutOptions.Center;
        var titleRow = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star)],
            Children = { back, header },
        };
        Grid.SetColumn(header, 1);

        var readout = new Label
        {
            TextColor = UiTokens.InkSoft,
            FontFamily = DsChrome.PixelFont,
            FontSize = UiTokens.TextBody,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        readout.SetBinding(Label.TextProperty, new Binding(nameof(RetroAchievementsViewModel.Status), converter: Kit.TidyText));

        var refresh = Kit.Capsule(AppStrings.Fr ? "Rafraîchir" : "Refresh", UiTokens.Cyan);
        refresh.Clicked += (_, _) => _ = RefreshAsync();

        _signInPanel = BuildSignInPanel();

        _list = new CollectionView
        {
            SelectionMode = SelectionMode.Single,
            ItemTemplate = new DataTemplate(() => BuildGameCard()),
        };
        _list.SetBinding(ItemsView.ItemsSourceProperty, nameof(RetroAchievementsViewModel.Games));
        _list.SelectionChanged += (_, _) => { };

        var body = new Grid
        {
            RowSpacing = 10,
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star)],
            Children = { Kit.Well(readout, padding: 8), _signInPanel, _list },
        };
        Grid.SetRow(_signInPanel, 1);
        Grid.SetRow(_list, 2);

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

    private View BuildSignInPanel()
    {
        var userEntry = new Entry
        {
            FontSize = 16,
            FontFamily = DsChrome.PixelFont,
            TextColor = UiTokens.Ink0,
            BackgroundColor = UiTokens.ShellPress,
            Placeholder = AppStrings.RaUsername,
            PlaceholderColor = UiTokens.Ink1,
            IsSpellCheckEnabled = false,
            IsTextPredictionEnabled = false,
            ReturnType = ReturnType.Next,
        };
        var keyEntry = new Entry
        {
            FontSize = 16,
            FontFamily = DsChrome.PixelFont,
            TextColor = UiTokens.Ink0,
            BackgroundColor = UiTokens.ShellPress,
            Placeholder = AppStrings.RaApiKey,
            PlaceholderColor = UiTokens.Ink1,
            IsPassword = true,
            IsSpellCheckEnabled = false,
            IsTextPredictionEnabled = false,
            ReturnType = ReturnType.Done,
        };
        var signIn = Kit.Capsule(AppStrings.SignIn, UiTokens.Green);
        signIn.Clicked += async (_, _) =>
        {
            _viewModel.RememberUsername(userEntry.Text ?? "");
            await _viewModel.SignInCommand.ExecuteAsync(keyEntry.Text);
            UpdateSignInState();
            await RefreshAsync();
        };
        var signOut = Kit.Capsule(AppStrings.SignOut, UiTokens.Ink1);
        signOut.Clicked += (_, _) =>
        {
            _viewModel.SignOutCommand.Execute(null);
            UpdateSignInState();
        };
        var row = new HorizontalStackLayout { Spacing = 8, Children = { signIn, signOut } };
        return Kit.Well(new VerticalStackLayout
        {
            Spacing = 8,
            Children = { userEntry, keyEntry, row },
        }, padding: 8);
    }

    private void UpdateSignInState()
    {
        _signInPanel.IsVisible = !_viewModel.SignedIn;
        if (_viewModel.SignedIn) _viewModel.Status = AppStrings.SignedInAs(_viewModel.CurrentUsername ?? "");
    }

    private static View BuildGameCard()
    {
        // A card per game: title, completion percent, and the achievement rows beneath.
        var title = new Label { TextColor = UiTokens.Ink0, FontFamily = DsChrome.PixelFont, FontSize = 15 };
        title.SetBinding(Label.TextProperty, "Title");

        var percent = new Label { TextColor = UiTokens.Cyan, FontFamily = DsChrome.PixelFont, FontSize = 13 };
        percent.SetBinding(Label.TextProperty, new Binding("Percent", stringFormat: "{0}%"));

        var achievements = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            ItemTemplate = new DataTemplate(BuildAchievementRow),
        };
        achievements.SetBinding(ItemsView.ItemsSourceProperty, "Achievements");

        return Kit.Well(new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                new Grid
                {
                    ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)],
                    Children = { title, percent },
                },
                achievements,
            },
        }, padding: 10);
    }

    private static View BuildAchievementRow()
    {
        var glyph = new Label
        {
            Text = "\u25cb",
            FontFamily = DsChrome.PixelFont,
            FontSize = 14,
            VerticalOptions = LayoutOptions.Center,
            WidthRequest = 22,
        };
        glyph.SetBinding(Label.TextProperty, new Binding(nameof(RetroAchievement.Earned), converter: EarnedGlyphConverter.Instance));
        glyph.SetBinding(Label.TextColorProperty, new Binding(nameof(RetroAchievement.Earned), converter: EarnedGlyphColorConverter.Instance));
        var title = new Label { TextColor = UiTokens.Ink0, FontFamily = DsChrome.PixelFont, FontSize = 13, LineBreakMode = LineBreakMode.TailTruncation };
        title.SetBinding(Label.TextProperty, nameof(RetroAchievement.Title));
        var detail = new Label { TextColor = UiTokens.InkSoft, FontFamily = DsChrome.PixelFont, FontSize = 11, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 2 };
        detail.SetBinding(Label.TextProperty, nameof(RetroAchievement.Description));
        var points = new Label { TextColor = UiTokens.Ink1, FontFamily = DsChrome.PixelFont, FontSize = 12, VerticalOptions = LayoutOptions.Center };
        points.SetBinding(Label.TextProperty, new Binding(nameof(RetroAchievement.Points), stringFormat: "+{0}"));

        return new Grid
        {
            ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)],
            ColumnSpacing = 6,
            Children = { glyph, title, detail, points },
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadCached();
        UpdateSignInState();
        IPlatformApplication.Current?.Services.GetService<GamepadRouter>()?.Push(this);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        IPlatformApplication.Current?.Services.GetService<GamepadRouter>()?.Remove(this);
    }

    private async Task RefreshAsync()
    {
        try
        {
            await _viewModel.RefreshForSavesAsync(_saves.Saves.ToList());
        }
        catch (Exception error)
        {
            _viewModel.Status = AppStrings.RaRefreshFailed(error.Message);
        }
    }

    public bool OnPadButton(PadButton button)
    {
        switch (button)
        {
            case PadButton.Up: return MoveCursor(-1);
            case PadButton.Down: return MoveCursor(1);
            case PadButton.A: _ = RefreshAsync(); return true;
            case PadButton.B: _ = Navigation.PopAsync(); return true;
            default: return true;
        }
    }

    private bool MoveCursor(int delta)
    {
        if (_viewModel.Games.Count == 0) return true;
        _index = Math.Clamp(_index < 0 ? 0 : _index + delta, 0, _viewModel.Games.Count - 1);
        _list.SelectedItem = _viewModel.Games[_index];
        _list.ScrollTo(_index, position: ScrollToPosition.MakeVisible, animate: false);
        return true;
    }
}

/// <summary>Turns an achievement's earned flag into the Pokéball glyph row: caught or empty.</summary>
public sealed class EarnedGlyphConverter : IValueConverter
{
    public static readonly EarnedGlyphConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        value is true ? "\u2714" : "\u25cb";

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Green when the achievement is unlocked, muted ink while it is locked.</summary>
public sealed class EarnedGlyphColorConverter : IValueConverter
{
    public static readonly EarnedGlyphColorConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        value is true ? UiTokens.Green : UiTokens.Ink1;

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}
