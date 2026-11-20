using PKForge.App.Platforms.Android;
using PKForge.App.Services;
using PKForge.App.Theme;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace PKForge.App.Views;

/// <summary>
/// The embedded game screen: mGBA or melonDS renders in-process and this page
/// scales the framebuffer to the available screen while preserving its aspect ratio.
/// </summary>
public sealed class EmbeddedGamePage : ContentPage, IPadHandler, IPadReleaseHandler
{
    private readonly SKCanvasView _canvas = new() { IgnorePixelScaling = true };
    private readonly IEmbeddedGameSession _session;
    private readonly IDualScreenGameDisplayHost? _dualScreenHost;
    private readonly Dictionary<PadButton, uint> _held = [];
    private readonly HashSet<PadButton> _touchHeld = [];
    private readonly object _heldGate = new();
    private readonly Button _pauseButton;
    private readonly Button _optionsButton;
    private readonly Button _floatingOptionsButton;
    private readonly Button _loadStateButton;
    private readonly Grid _header;
    private readonly VerticalStackLayout _footer;
    private Grid _touchControls = null!;
    private Grid _hostGrid = null!;
    private bool _exitStarted;
    private volatile bool _pageVisible;
    private bool _usingSeparateDsDisplays;
    private Domain.GameCompanion? _companionOwner;
    private GameCompanionStore? _companionStore;
    private readonly Label _status = new()
    {
        FontFamily = "PixelUI",
        FontSize = 10,
        TextColor = Colors.LightGray,
        HorizontalTextAlignment = TextAlignment.Center,
    };
    private bool _userPaused;
    private Task<bool>? _suspendSaveTask;
    private SKRect _gameRect;
    private long _stylusPointerId = -1;

    public EmbeddedGamePage(IEmbeddedGameSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _dualScreenHost = IPlatformApplication.Current?.Services.GetService<IDualScreenGameDisplayHost>();
        BackgroundColor = Colors.Black;
        _session.SetSpeedMultiplier(EmbeddedGamePreferences.SpeedMultiplier);

        _pauseButton = ActionButton(AppStrings.GamePause);
        _optionsButton = ActionButton(AppStrings.GameOptions);
        _floatingOptionsButton = new Button
        {
            Text = "⚙",
            FontSize = 18,
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#AA17141F"),
            BorderColor = Color.FromArgb("#B0FFFFFF"),
            BorderWidth = 1,
            CornerRadius = 20,
            Padding = new Thickness(6, 0),
            WidthRequest = 72,
            HeightRequest = 42,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(10),
        };
        _loadStateButton = ActionButton(AppStrings.GameLoadState);
        _pauseButton.Clicked += (_, _) => TogglePause();
        _optionsButton.Clicked += async (_, _) => await ShowOptionsAsync();
        _floatingOptionsButton.Clicked += async (_, _) => await ShowOptionsAsync();
        _loadStateButton.Clicked += async (_, _) => await LoadStateAsync();
        UpdateSpeedIndicators();

        var saveStateButton = ActionButton(AppStrings.GameSaveState);
        saveStateButton.Clicked += async (_, _) => await SaveStateAsync();
        var exitButton = ActionButton(AppStrings.GameExit);
        exitButton.Clicked += async (_, _) => await ExitGameAsync();

        _header = new Grid
        {
            Padding = new Thickness(12, 6),
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto)],
            ColumnSpacing = 8,
            BackgroundColor = Color.FromArgb("#17141F"),
            Children =
            {
                new Label
                {
                    Text = string.IsNullOrWhiteSpace(_session.GameTitle) ? "PKForge" : _session.GameTitle,
                    FontFamily = "PixelUI",
                    FontSize = 13,
                    TextColor = Colors.White,
                    VerticalTextAlignment = TextAlignment.Center,
                    LineBreakMode = LineBreakMode.TailTruncation,
                },
                _pauseButton,
                _optionsButton,
                exitButton,
            },
        };
        Grid.SetColumn(_pauseButton, 1);
        Grid.SetColumn(_optionsButton, 2);
        Grid.SetColumn(exitButton, 3);

        var actions = new HorizontalStackLayout
        {
            Spacing = 8,
            HorizontalOptions = LayoutOptions.Center,
            Children = { saveStateButton, _loadStateButton },
        };
        _footer = new VerticalStackLayout
        {
            Spacing = 4,
            Padding = new Thickness(8, 6),
            BackgroundColor = Color.FromArgb("#17141F"),
            Children = { actions },
        };

        _touchControls = BuildTouchControls();
        _status.HorizontalOptions = LayoutOptions.Center;
        _status.VerticalOptions = LayoutOptions.Start;
        _status.Margin = new Thickness(48, 12, 48, 0);
        _status.Padding = new Thickness(8, 4);
        _status.BackgroundColor = Color.FromArgb("#AA17141F");
        _status.IsVisible = false;
        var playfield = new Grid { Children = { _canvas, _touchControls, _status, _floatingOptionsButton } };
        var layout = new Grid
        {
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)],
            Children = { _header, playfield, _footer },
        };
        _hostGrid = layout;
        Grid.SetRow(playfield, 1);
        Grid.SetRow(_footer, 2);
        Content = layout;

        _touchControls.IsVisible = EmbeddedGameDisplayPreferences.TouchControlsVisible;
        _floatingOptionsButton.IsVisible = EmbeddedGameDisplayPreferences.FullScreen;
        _header.IsVisible = !EmbeddedGameDisplayPreferences.FullScreen;
        _footer.IsVisible = !EmbeddedGameDisplayPreferences.FullScreen;
        NavigationPage.SetHasNavigationBar(this, !EmbeddedGameDisplayPreferences.FullScreen);

        _canvas.PaintSurface += OnPaint;
        _canvas.EnableTouchEvents = session.SupportsTouchScreen;
        if (session.SupportsTouchScreen) _canvas.Touch += OnCanvasTouch;
        _session.FrameProduced += Invalidate;
        UpdateStateActions();
    }

    private static Button ActionButton(string text) => new()
    {
        Text = text,
        FontFamily = "PixelUI",
        FontSize = 10,
        TextColor = Colors.White,
        BackgroundColor = Color.FromArgb("#342F43"),
        CornerRadius = 6,
        Padding = new Thickness(10, 5),
        MinimumHeightRequest = 36,
    };

    private Grid BuildTouchControls()
    {
        var directions = new Grid
        {
            WidthRequest = 156,
            HeightRequest = 156,
            RowDefinitions = [new(48), new(48), new(48)],
            ColumnDefinitions = [new(48), new(48), new(48)],
            RowSpacing = 4,
            ColumnSpacing = 4,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.End,
        };
        AddTouchButton(directions, PadButton.Up, "↑", 0, 1, 48);
        AddTouchButton(directions, PadButton.Left, "←", 1, 0, 48);
        AddTouchButton(directions, PadButton.Right, "→", 1, 2, 48);
        AddTouchButton(directions, PadButton.Down, "↓", 2, 1, 48);

        var actions = new Grid
        {
            WidthRequest = 128,
            HeightRequest = 128,
            RowDefinitions = [new(60), new(60)],
            ColumnDefinitions = [new(60), new(60)],
            RowSpacing = 8,
            ColumnSpacing = 8,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.End,
        };
        AddTouchButton(actions, PadButton.B, "B", 1, 0, 60);
        AddTouchButton(actions, PadButton.A, "A", 0, 1, 60);
        if (_session.IsNintendoDs)
        {
            AddTouchButton(actions, PadButton.X, "X", 0, 0, 60);
            AddTouchButton(actions, PadButton.Y, "Y", 1, 1, 60);
        }

        var systemButtons = new HorizontalStackLayout
        {
            Spacing = 8,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.End,
            Children =
            {
                TouchButton(PadButton.Select, "Select", 72, 38, 8),
                TouchButton(PadButton.Start, "Start", 72, 38, 8),
            },
        };

        var overlay = new Grid
        {
            Padding = new Thickness(12),
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto), new(GridLength.Star)],
            Children = { directions, systemButtons, actions },
        };
        Grid.SetColumn(systemButtons, 1);
        Grid.SetColumn(actions, 2);
        return overlay;
    }

    private void AddTouchButton(Grid grid, PadButton button, string label, int row, int column, double size)
    {
        var view = TouchButton(button, label, size, size, (int)(size / 2));
        grid.Children.Add(view);
        Grid.SetRow(view, row);
        Grid.SetColumn(view, column);
    }

    private Button TouchButton(PadButton button, string label, double width, double height, int cornerRadius)
    {
        var view = new Button
        {
            Text = label,
            FontFamily = "PixelUI",
            FontSize = 11,
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#AA342F43"),
            BorderColor = Color.FromArgb("#B0FFFFFF"),
            BorderWidth = 1,
            CornerRadius = cornerRadius,
            Padding = 0,
            WidthRequest = width,
            HeightRequest = height,
            MinimumWidthRequest = width,
            MinimumHeightRequest = height,
        };
        view.Pressed += (_, _) => SetTouchButton(button, true);
        view.Released += (_, _) => SetTouchButton(button, false);
        return view;
    }

    private void ToggleTouchControls()
    {
        _touchControls.IsVisible = !_touchControls.IsVisible;
        EmbeddedGameDisplayPreferences.TouchControlsVisible = _touchControls.IsVisible;
        if (!_touchControls.IsVisible) ReleaseTouchButtons();
    }

    private void ToggleFullScreen()
    {
        EmbeddedGameDisplayPreferences.FullScreen = !EmbeddedGameDisplayPreferences.FullScreen;
        var fullScreen = EmbeddedGameDisplayPreferences.FullScreen;
        _header.IsVisible = !fullScreen;
        _footer.IsVisible = !fullScreen;
        _floatingOptionsButton.IsVisible = fullScreen;
        NavigationPage.SetHasNavigationBar(this, !fullScreen);
    }

    private async Task ShowOptionsAsync()
    {
        var options = new List<string>
        {
            _userPaused ? AppStrings.GameResume : AppStrings.GamePause,
            AppStrings.GameSpeedMenuLabel(EmbeddedGamePreferences.SpeedMultiplier),
            AppStrings.GameDisplay,
            EmbeddedGameDisplayPreferences.FullScreen ? AppStrings.GameExitFullScreen : AppStrings.GameEnterFullScreen,
            _touchControls.IsVisible ? AppStrings.GameHideTouchControls : AppStrings.GameShowTouchControls,
            AppStrings.GameSaveState,
        };
        if (_session.HasQuickState) options.Add(AppStrings.GameLoadState);
        options.Add(AppStrings.GameExit);

        var choice = await PadMenu.ShowAsync(_hostGrid, AppStrings.GameOptions, null, [.. options]);
        switch (choice)
        {
            case var value when value == (_userPaused ? AppStrings.GameResume : AppStrings.GamePause):
                TogglePause();
                break;
            case var value when value == AppStrings.GameSpeedMenuLabel(EmbeddedGamePreferences.SpeedMultiplier):
                await ShowSpeedSettingsAsync();
                break;
            case var value when value == AppStrings.GameDisplay:
                await ShowDisplaySettingsAsync();
                break;
            case var value when value == (EmbeddedGameDisplayPreferences.FullScreen ? AppStrings.GameExitFullScreen : AppStrings.GameEnterFullScreen):
                ToggleFullScreen();
                break;
            case var value when value == (_touchControls.IsVisible ? AppStrings.GameHideTouchControls : AppStrings.GameShowTouchControls):
                ToggleTouchControls();
                break;
            case var value when value == AppStrings.GameSaveState:
                await SaveStateAsync();
                break;
            case var value when value == AppStrings.GameLoadState:
                await LoadStateAsync();
                break;
            case var value when value == AppStrings.GameExit:
                await ExitGameAsync();
                break;
        }
    }

    private async Task ShowSpeedSettingsAsync()
    {
        var current = EmbeddedGamePreferences.SpeedMultiplier;
        var options = Enumerable.Range(1, 4)
            .Select(multiplier => new PadOption(
                AppStrings.GameSpeedChoice(multiplier),
                multiplier == current ? "✓" : null,
                multiplier == current ? UiTokens.Cyan : null))
            .ToArray();
        var selected = await PadMenu.ShowAtAsync(
            _hostGrid, AppStrings.GameSpeed, AppStrings.GameSpeedHelp, current - 1, options);
        if (selected is null) return;

        var selectedIndex = Array.FindIndex(options, option => option.Label == selected);
        if ((uint)selectedIndex >= (uint)options.Length) return;
        var multiplier = selectedIndex + 1;
        ApplySpeedMultiplier(multiplier);
    }

    private void ApplySpeedMultiplier(int multiplier)
    {
        EmbeddedGamePreferences.SpeedMultiplier = multiplier;
        _session.SetSpeedMultiplier(multiplier);
        _companionStore?.UpdateSpeedMultiplier(multiplier);
        UpdateSpeedIndicators();
    }

    private void UpdateSpeedIndicators()
    {
        var multiplier = EmbeddedGamePreferences.SpeedMultiplier;
        _optionsButton.Text = AppStrings.GameOptionsSpeed(multiplier);
        _floatingOptionsButton.Text = $"⚙ ×{multiplier}";
    }

    private async Task ShowDisplaySettingsAsync()
    {
        var modes = new (EmbeddedGameScaleMode Mode, string Label)[]
        {
            (EmbeddedGameScaleMode.Fit, AppStrings.GameScaleFit),
            (EmbeddedGameScaleMode.NinetyPercent, AppStrings.GameScale90),
            (EmbeddedGameScaleMode.SeventyFivePercent, AppStrings.GameScale75),
            (EmbeddedGameScaleMode.PixelPerfect, AppStrings.GameScalePixelPerfect),
        };
        var currentIndex = Array.FindIndex(modes, item => item.Mode == EmbeddedGameDisplayPreferences.ScaleMode);
        var options = modes.Select((item, index) =>
            new PadOption(item.Label, index == currentIndex ? "✓" : null, index == currentIndex ? UiTokens.Cyan : null)).ToArray();
        var selected = await PadMenu.ShowAtAsync(_hostGrid, AppStrings.GameDisplay, AppStrings.GameDisplayHelp,
            Math.Max(0, currentIndex), options);
        if (selected is null) return;
        EmbeddedGameDisplayPreferences.ScaleMode = selected switch
        {
            var value when value == AppStrings.GameScale90 => EmbeddedGameScaleMode.NinetyPercent,
            var value when value == AppStrings.GameScale75 => EmbeddedGameScaleMode.SeventyFivePercent,
            var value when value == AppStrings.GameScalePixelPerfect => EmbeddedGameScaleMode.PixelPerfect,
            _ => EmbeddedGameScaleMode.Fit,
        };
        _canvas.InvalidateSurface();
    }

    private void SetTouchButton(PadButton button, bool pressed)
    {
        lock (_heldGate)
        {
            if (pressed) _touchHeld.Add(button);
            else _touchHeld.Remove(button);
            UpdateKeysLocked();
        }
    }

    private void ReleaseTouchButtons()
    {
        lock (_heldGate)
        {
            _touchHeld.Clear();
            UpdateKeysLocked();
        }
    }

    private void ReleaseAllButtons()
    {
        lock (_heldGate)
        {
            _held.Clear();
            _touchHeld.Clear();
            UpdateKeysLocked();
        }
    }

    private void UpdateKeysLocked()
    {
        var keys = _held.Values.Aggregate(0u, (mask, value) => mask | value);
        foreach (var button in _touchHeld) keys |= BitOf(button);
        _session.SetKeys(keys);
    }

    private void TogglePause()
    {
        _userPaused = !_userPaused;
        _session.SetPaused(_userPaused);
        _pauseButton.Text = _userPaused ? AppStrings.GameResume : AppStrings.GamePause;
        SetStatus(_userPaused ? AppStrings.GamePaused : string.Empty);
    }

    private async Task SaveStateAsync()
    {
        SetStatus(AppStrings.GameSavingState);
        var saved = await Task.Run(_session.SaveQuickState);
        SetStatus(saved ? AppStrings.GameStateSaved : AppStrings.GameStateSaveFailed);
        UpdateStateActions();
    }

    private async Task LoadStateAsync()
    {
        SetStatus(AppStrings.GameLoadingState);
        var loaded = await Task.Run(_session.LoadQuickState);
        SetStatus(loaded ? AppStrings.GameStateLoaded : AppStrings.GameStateLoadFailed);
        UpdateStateActions();
    }

    private void UpdateStateActions() => _loadStateButton.IsEnabled = _session.HasQuickState;

    private void SetStatus(string text)
    {
        _status.Text = text;
        _status.IsVisible = !string.IsNullOrWhiteSpace(text);
    }

    private async Task ExitGameAsync()
    {
        if (_exitStarted) return;
        _exitStarted = true;
        _pauseButton.IsEnabled = false;
        if (!await _session.CloseAsync())
            await PadMenu.ShowAsync(_hostGrid, AppStrings.GameBatterySaveFailedTitle,
                _session.HasSaveConflict ? AppStrings.GameSaveConflict : AppStrings.GameBatterySaveFailed,
                AppStrings.Ok);
        await Navigation.PopAsync();
    }

    protected override bool OnBackButtonPressed()
    {
        _ = ExitGameAsync();
        return true;
    }

    private void OnAppSuspended()
    {
        ReleaseAllButtons();
        _session.SetTouchScreen(false, 0, 0);
        _stylusPointerId = -1;
        _suspendSaveTask = _session.FlushBatterySaveAsync();
    }

    private async void OnAppResumed()
    {
        var syncTask = _suspendSaveTask;
        _suspendSaveTask = null;
        if (syncTask is not null && !await syncTask)
            SetStatus(_session.HasSaveConflict ? AppStrings.GameSaveConflict : AppStrings.GameBatterySaveFailed);
        if (!_userPaused) _session.SetPaused(false);
    }

    private void Invalidate() => MainThread.InvokeOnMainThreadAsync(_canvas.InvalidateSurface);

    /// <summary>Maps the shared pad into each console's hardware key bits.</summary>
    private uint BitOf(PadButton button) => _session.IsNintendoDs ? button switch
    {
        PadButton.A => Domain.NdsKeys.A,
        PadButton.B => Domain.NdsKeys.B,
        PadButton.Start => Domain.NdsKeys.Start,
        PadButton.Select => Domain.NdsKeys.Select,
        PadButton.L => Domain.NdsKeys.L,
        PadButton.R => Domain.NdsKeys.R,
        PadButton.X => Domain.NdsKeys.X,
        PadButton.Y => Domain.NdsKeys.Y,
        PadButton.Up => Domain.NdsKeys.Up,
        PadButton.Down => Domain.NdsKeys.Down,
        PadButton.Left => Domain.NdsKeys.Left,
        PadButton.Right => Domain.NdsKeys.Right,
        _ => 0,
    } : button switch
    {
        PadButton.A => Domain.GbaKeys.A,
        PadButton.B => Domain.GbaKeys.B,
        PadButton.X => Domain.GbaKeys.B,        // GBA has no X: both map to B
        PadButton.Y => Domain.GbaKeys.A,        // ditto Y → A
        PadButton.Start => Domain.GbaKeys.Start,
        PadButton.Select => Domain.GbaKeys.Select,
        PadButton.L => Domain.GbaKeys.L,
        PadButton.R => Domain.GbaKeys.R,
        PadButton.Up => Domain.GbaKeys.Up,
        PadButton.Down => Domain.GbaKeys.Down,
        PadButton.Left => Domain.GbaKeys.Left,
        PadButton.Right => Domain.GbaKeys.Right,
        _ => 0,
    };

    /// <summary>IPadHandler: a press latches the bit into the held mask.</summary>
    public bool OnPadButton(PadButton button)
    {
        var bit = BitOf(button);
        if (bit == 0) return false;
        lock (_heldGate)
        {
            _held[button] = bit;
            UpdateKeysLocked();
        }
        return true; // the game screen consumes every button
    }

    /// <summary>IPadReleaseHandler: a release clears the bit (hold gestures work).</summary>
    public void OnPadReleased(PadButton button)
    {
        lock (_heldGate)
        {
            _held.Remove(button);
            UpdateKeysLocked();
        }
    }

    private void OnPaint(object? sender, SKPaintSurfaceEventArgs args)
    {
        var canvas = args.Surface.Canvas;
        canvas.Clear(SKColors.Black);
        var splitDsScreens = _session.IsNintendoDs && _usingSeparateDsDisplays;
        var (width, height) = splitDsScreens ? (256, 192) : _session.VideoSize;
        if (width == 0 || height == 0) return;

        var maximumScale = Math.Min((float)args.Info.Width / width, (float)args.Info.Height / height);
        var scale = EmbeddedGameDisplayPreferences.ScaleFor(EmbeddedGameDisplayPreferences.ScaleMode, maximumScale);

        var drawWidth = width * scale;
        var drawHeight = height * scale;
        var left = (args.Info.Width - drawWidth) / 2f;
        var top = (args.Info.Height - drawHeight) / 2f;
        _gameRect = new SKRect(left, top, left + drawWidth, top + drawHeight);
        if (splitDsScreens) _session.DrawMainScreen(canvas, _gameRect);
        else _session.Draw(canvas, _gameRect);
    }

    private void OnCanvasTouch(object? sender, SKTouchEventArgs args)
    {
        if (!_session.SupportsTouchScreen || _usingSeparateDsDisplays) return;
        switch (args.ActionType)
        {
            case SKTouchAction.Pressed:
                _stylusPointerId = args.Id;
                UpdateStylus(args.Location);
                args.Handled = true;
                break;
            case SKTouchAction.Moved when args.Id == _stylusPointerId:
                UpdateStylus(args.Location);
                args.Handled = true;
                break;
            case SKTouchAction.Released or SKTouchAction.Cancelled when args.Id == _stylusPointerId:
                _stylusPointerId = -1;
                _session.SetTouchScreen(false, 0, 0);
                args.Handled = true;
                break;
        }
    }

    private void UpdateStylus(SKPoint location)
    {
        var (width, height) = _session.VideoSize;
        if (!_gameRect.Contains(location.X, location.Y) || width != 256 || height != 384)
        {
            _session.SetTouchScreen(false, 0, 0);
            return;
        }

        var lowerScreenTop = _gameRect.Top + _gameRect.Height / 2f;
        if (location.Y < lowerScreenTop)
        {
            _session.SetTouchScreen(false, 0, 0);
            return;
        }

        var x = (int)((location.X - _gameRect.Left) * 256f / _gameRect.Width);
        var y = (int)((location.Y - lowerScreenTop) * 192f / (_gameRect.Height / 2f));
        _session.SetTouchScreen(true, Math.Clamp(x, 0, 255), Math.Clamp(y, 0, 191));
    }

    private async Task ShowDsBottomScreenAsync()
    {
        if (_dualScreenHost is null || !_pageVisible) return;
        try
        {
            await _dualScreenHost.ShowNintendoDsBottomScreenAsync(_session);
            OnSecondaryDisplayContentChanged();
        }
        catch (Exception error)
        {
            Services.AppLog.Warn("second", $"Could not route the DS lower screen to the second display; keeping both screens on the main display: {error.Message}");
            SetSeparateDsDisplays(false);
        }
    }

    private void OnSecondaryDisplayContentChanged()
    {
        if (!_pageVisible || _dualScreenHost is null || !_session.IsNintendoDs) return;
        SetSeparateDsDisplays(_dualScreenHost.IsShowingNintendoDsBottomScreen(_session));
    }

    private void SetSeparateDsDisplays(bool enabled)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_usingSeparateDsDisplays == enabled) return;
            _usingSeparateDsDisplays = enabled;
            if (enabled && _stylusPointerId != -1)
            {
                _stylusPointerId = -1;
                _session.SetTouchScreen(false, 0, 0);
            }
            _canvas.InvalidateSurface();
        });
    }

    private Timer? _liveTimer;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _pageVisible = true;
        App.Suspended += OnAppSuspended;
        App.Resumed += OnAppResumed;
        _session.SetPaused(_userPaused);
        if (_session.HasSaveConflict) SetStatus(AppStrings.GameSaveConflict);
        IPlatformApplication.Current?.Services.GetService<GamepadRouter>()?.Push(this);
        if (_session.IsNintendoDs && _dualScreenHost is not null)
        {
            _dualScreenHost.DisplayContentChanged += OnSecondaryDisplayContentChanged;
            _ = ShowDsBottomScreenAsync();
        }
        // Refresh companion values from mGBA's battery-save image. Until we read the
        // game's emulated RAM directly, these values reflect the last in-game save.
        var store = IPlatformApplication.Current?.Services.GetService<GameCompanionStore>();
        if (store is not null && _session.SupportsLiveCompanion)
        {
            var owner = new Domain.GameCompanion(_session.GameTitle, _session.Generation, 0, 0, 0, []);
            _companionOwner = owner;
            _companionStore = store;
            store.Start(owner);
            store.BindSpeedControl(EmbeddedGamePreferences.SpeedMultiplier, ApplySpeedMultiplier);
            _liveTimer = new Timer(_ =>
            {
                if (!_pageVisible || !ReferenceEquals(store.Current, owner)) return;
                var snapshot = _session.ReadLiveCompanion();
                if (snapshot is null) return;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (!_pageVisible || !ReferenceEquals(store.Current, owner)) return;
                    store.UpdateLive(snapshot);
                });
            }, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _pageVisible = false;
        App.Suspended -= OnAppSuspended;
        App.Resumed -= OnAppResumed;
        if (_dualScreenHost is not null)
        {
            _dualScreenHost.DisplayContentChanged -= OnSecondaryDisplayContentChanged;
            _ = _dualScreenHost.RestoreDefaultScreenAsync(_session);
        }
        _session.FrameProduced -= Invalidate;
        ReleaseAllButtons();
        _session.SetTouchScreen(false, 0, 0);
        IPlatformApplication.Current?.Services.GetService<GamepadRouter>()?.Remove(this);
        _liveTimer?.Dispose();
        _liveTimer = null;
        var companionStore = IPlatformApplication.Current?.Services.GetService<GameCompanionStore>();
        if (companionStore is not null && ReferenceEquals(companionStore.Current, _companionOwner))
        {
            companionStore.ClearSpeedControl();
            companionStore.ClearLive();
        }
        _companionStore = null;
        _companionOwner = null;
        _ = _session.CloseAsync();
    }
}
