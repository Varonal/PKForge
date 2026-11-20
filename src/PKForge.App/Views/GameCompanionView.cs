using PKForge.App.Services;
using PKForge.App.Theme;
using PKForge.Domain;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace PKForge.App.Views;

/// <summary>The configurable lower-screen companion for embedded and external games.</summary>
public sealed class GameCompanionView : VerticalStackLayout
{
    private readonly Label _title = new()
    {
        FontFamily = DsChrome.PixelFont,
        FontSize = 15,
        TextColor = UiTokens.Ink0,
        HorizontalTextAlignment = TextAlignment.Center,
        LineBreakMode = LineBreakMode.TailTruncation,
    };
    private readonly Grid _navigation = new() { ColumnSpacing = 4 };
    private readonly VerticalStackLayout _content = new() { Spacing = 8 };
    private readonly ScrollView _contentScroll;
    private readonly Label _externalSummary = new()
    {
        FontFamily = DsChrome.PixelFont,
        FontSize = 10,
        TextColor = UiTokens.Cyan,
        HorizontalTextAlignment = TextAlignment.Center,
        IsVisible = false,
    };
    private GameCompanion? _companion;
    private LiveCompanionSnapshot? _live;
    private GameCompanionTab _selectedTab;
    private string? _selectedBagPocketId;
    private bool _showSettings;
    private bool _hasShown;

    public GameCompanionView()
    {
        Spacing = 8;
        Margin = new Thickness(10, 8);
        Padding = new Thickness(8);
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;
        _contentScroll = new ScrollView { Content = _content, VerticalOptions = LayoutOptions.Fill };
        Children.Add(_title);
        Children.Add(_externalSummary);
        Children.Add(_navigation);
        Children.Add(_contentScroll);
        IsVisible = false;
    }

    public void Show(GameCompanion companion)
    {
        var newGame = !ReferenceEquals(_companion, companion);
        _companion = companion;
        _title.Text = companion.GameLabel;
        _externalSummary.IsVisible = false;
        _externalSummary.Text = string.Empty;
        if (!newGame && _hasShown)
        {
            if (_showSettings) Render();
            IsVisible = true;
            return;
        }
        if (newGame || !_hasShown)
        {
            if (newGame) _live = null;
            _selectedTab = GameCompanionPreferences.HomeTab;
            _selectedBagPocketId = null;
            _showSettings = false;
            _hasShown = true;
        }
        else if (!GameCompanionPreferences.VisibleTabs.Contains(_selectedTab))
            _selectedTab = GameCompanionPreferences.HomeTab;
        Render();
        IsVisible = true;
    }

    public void ShowLive(LiveCompanionSnapshot live)
    {
        if (_live is { } current && HaveSameSnapshot(current, live))
            return;

        _live = live;
        if (!_showSettings && _selectedTab is GameCompanionTab.Party or GameCompanionTab.Bag
            or GameCompanionTab.Pokedex or GameCompanionTab.Session)
            Render();
    }

    /// <summary>
    /// Overlays a snapshot polled from the external emulator's save file (US-003), with the
    /// session chrono and the save's own playtime (US-006). Read-only: the companion never
    /// touches the save the emulator holds open, and it lives on the lower screen only.
    /// </summary>
    public void ShowExternal(ExternalCompanionSnapshot external, PlaySessionStats? session)
    {
        var save = AppStrings.PlaytimeHours(external.PlaytimeHours, external.PlaytimeMinutes);
        var party = AppStrings.Fr ? "équipe" : "party";
        var line = $"{AppStrings.DexProgressLabel} {external.DexCaught}/{external.DexTotal} · "
            + $"{external.DexPercent}% · {party} {external.PartyCount}/6 · {AppStrings.SavePlaytime} {save}";
        if (session is not null)
        {
            var now = DateTimeOffset.UtcNow;
            var sessionLine = PlaySessionStats.FormatDuration(session.SessionElapsed(now));
            var totalLine = PlaySessionStats.FormatDuration(session.GrandTotal(now));
            line += $" · {AppStrings.SessionTime} {sessionLine} · {AppStrings.TotalPlaytime} {totalLine}";
        }
        _externalSummary.Text = line;
        _externalSummary.IsVisible = true;
    }

    private static bool HaveSameSnapshot(LiveCompanionSnapshot left, LiveCompanionSnapshot right) =>
        left.GameLabel == right.GameLabel
        && left.GameCode == right.GameCode
        && left.DexCaught == right.DexCaught
        && left.DexSeen == right.DexSeen
        && left.DexTotal == right.DexTotal
        && left.PartyCount == right.PartyCount
        && left.PlaytimeHours == right.PlaytimeHours
        && left.PlaytimeMinutes == right.PlaytimeMinutes
        && left.PartyMembers.SequenceEqual(right.PartyMembers)
        && left.BagPockets.Count == right.BagPockets.Count
        && left.BagPockets.Zip(right.BagPockets).All(pair =>
            pair.First.Id == pair.Second.Id
            && pair.First.Name == pair.Second.Name
            && pair.First.Items.SequenceEqual(pair.Second.Items));

    private void Render()
    {
        RenderNavigation();
        _content.Children.Clear();
        if (_showSettings)
        {
            RenderSettings();
            return;
        }

        switch (_selectedTab)
        {
            case GameCompanionTab.Party:
                RenderParty();
                break;
            case GameCompanionTab.Bag:
                RenderBag();
                break;
            case GameCompanionTab.Pokedex:
                RenderPokedex();
                break;
            case GameCompanionTab.Session:
                RenderSession();
                break;
            case GameCompanionTab.RestorePoints:
                RenderRestorePoints();
                break;
        }
    }

    private void RenderNavigation()
    {
        _navigation.Children.Clear();
        _navigation.ColumnDefinitions.Clear();
        var tabs = GameCompanionPreferences.VisibleTabs;
        for (var index = 0; index < tabs.Count + 1; index++)
            _navigation.ColumnDefinitions.Add(new ColumnDefinition(index == tabs.Count ? GridLength.Auto : GridLength.Star));

        for (var index = 0; index < tabs.Count; index++)
        {
            var tab = tabs[index];
            var button = NavigationButton(LabelFor(tab), !_showSettings && _selectedTab == tab);
            button.Clicked += (_, _) =>
            {
                _selectedTab = tab;
                _showSettings = false;
                Render();
            };
            _navigation.Add(button, index, 0);
        }

        var settings = NavigationButton("⚙", _showSettings);
        settings.WidthRequest = 40;
        settings.Clicked += (_, _) =>
        {
            _showSettings = !_showSettings;
            Render();
        };
        _navigation.Add(settings, tabs.Count, 0);
    }

    private void RenderParty()
    {
        if (_live is not { } live)
        {
            AddNote(AppStrings.CompanionNoSavedData);
            return;
        }

        var party = live.PartyMembers;
        if (party.Count == 0)
        {
            AddNote(AppStrings.CompanionSavedDataNotice);
            AddNote(AppStrings.CompanionNoParty);
            return;
        }

        AddNote(AppStrings.CompanionSavedDataNotice);

        foreach (var (member, index) in party.Select((member, index) => (member, index)))
        {
            var sprites = IPlatformApplication.Current?.Services.GetService<ISpriteService>();
            var sprite = new SKCanvasView
            {
                WidthRequest = 44,
                HeightRequest = 44,
                IgnorePixelScaling = true,
                InputTransparent = true,
            };
            sprite.PaintSurface += (_, args) =>
            {
                var canvas = args.Surface.Canvas;
                canvas.Clear(SKColors.Transparent);
                var bitmap = member.Species > 0
                    ? sprites?.GetSprite(member.Species, member.Form, member.IsShiny)
                    : null;
                if (bitmap is null)
                {
                    if (member.Species > 0)
                        sprites?.Warm(member.Species, member.Form, member.IsShiny,
                            () => MainThread.BeginInvokeOnMainThread(sprite.InvalidateSurface));
                    return;
                }

                var scale = Math.Min(args.Info.Width / (float)bitmap.Width, args.Info.Height / (float)bitmap.Height);
                var width = bitmap.Width * scale;
                var height = bitmap.Height * scale;
                var destination = new SKRect(
                    (args.Info.Width - width) / 2f,
                    (args.Info.Height - height) / 2f,
                    (args.Info.Width + width) / 2f,
                    (args.Info.Height + height) / 2f);
                canvas.DrawBitmap(bitmap, destination);
            };
            var heading = new Label
            {
                Text = AppStrings.CompanionPartyMember(
                    index + 1, member.Nickname, member.SpeciesName, member.Level, member.IsEgg),
                FontFamily = DsChrome.PixelFont,
                FontSize = 10,
                TextColor = UiTokens.Ink0,
                LineBreakMode = LineBreakMode.TailTruncation,
            };
            var hp = new Label
            {
                Text = AppStrings.CompanionHp(member.CurrentHp, member.MaxHp)
                    + (string.IsNullOrEmpty(member.Status) ? string.Empty : $" · {member.Status}"),
                FontFamily = DsChrome.PixelFont,
                FontSize = 9,
                TextColor = member.Status.Length == 0 ? UiTokens.InkSoft : UiTokens.RedOrange,
                VerticalTextAlignment = TextAlignment.Center,
            };
            var ratio = member.MaxHp <= 0 ? 0 : Math.Clamp((double)member.CurrentHp / member.MaxHp, 0, 1);
            var health = new ProgressBar
            {
                Progress = ratio,
                ProgressColor = ratio > 0.5 ? UiTokens.Green : ratio > 0.2 ? UiTokens.Yellow : UiTokens.RedOrange,
                BackgroundColor = UiTokens.ShellEdge,
                HeightRequest = 5,
                VerticalOptions = LayoutOptions.Center,
            };
            var row = new Grid
            {
                ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)],
                RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto)],
                ColumnSpacing = 8,
                RowSpacing = 3,
                Children = { sprite, heading, hp, health },
            };
            Grid.SetRowSpan(sprite, 2);
            Grid.SetColumn(heading, 1);
            Grid.SetColumn(hp, 2);
            Grid.SetRow(health, 1);
            Grid.SetColumn(health, 1);
            Grid.SetColumnSpan(health, 2);
            var rowBorder = new Border
            {
                Padding = new Thickness(8, 5),
                BackgroundColor = UiTokens.Paper,
                Stroke = UiTokens.ShellEdge,
                StrokeThickness = 1,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 5 },
                Content = row,
            };
            _content.Children.Add(rowBorder);
        }
    }

    private void RenderPokedex()
    {
        if (_live is not { } live)
        {
            AddNote(AppStrings.CompanionNoSavedData);
            return;
        }

        AddNote(AppStrings.CompanionSavedDataNotice);

        _content.Children.Add(new Label
        {
            Text = AppStrings.CompanionDex(live.DexCaught, live.DexSeen, live.DexTotal),
            FontFamily = DsChrome.PixelFont,
            FontSize = 11,
            TextColor = UiTokens.Cyan,
            HorizontalTextAlignment = TextAlignment.Center,
        });
        _content.Children.Add(new ProgressBar
        {
            Progress = Math.Clamp(live.DexPercent / 100d, 0, 1),
            ProgressColor = UiTokens.Cyan,
            BackgroundColor = UiTokens.ShellEdge,
        });
        _content.Children.Add(new Label
        {
            Text = $"{live.DexPercent}%",
            FontFamily = DsChrome.PixelFont,
            FontSize = 22,
            TextColor = UiTokens.Ink0,
            HorizontalTextAlignment = TextAlignment.Center,
        });
    }

    private void RenderBag()
    {
        if (_live is not { } live)
        {
            AddNote(AppStrings.CompanionNoSavedData);
            return;
        }

        AddNote(AppStrings.CompanionSavedDataNotice);
        var pockets = live.BagPockets;
        if (pockets.Count == 0)
        {
            AddNote(AppStrings.CompanionBagEmpty);
            return;
        }

        var pocketButtons = new HorizontalStackLayout { Spacing = 4 };
        var allButton = NavigationButton(AppStrings.CompanionPocketAll, _selectedBagPocketId is null);
        allButton.Clicked += (_, _) =>
        {
            _selectedBagPocketId = null;
            Render();
        };
        pocketButtons.Children.Add(allButton);

        foreach (var pocket in pockets)
        {
            var button = NavigationButton(pocket.Name, _selectedBagPocketId == pocket.Id);
            button.Clicked += (_, _) =>
            {
                _selectedBagPocketId = pocket.Id;
                Render();
            };
            pocketButtons.Children.Add(button);
        }

        _content.Children.Add(new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalOptions = LayoutOptions.Fill,
            Content = pocketButtons,
        });

        IEnumerable<GameCompanionBagPocket> visiblePockets = _selectedBagPocketId is null
            ? pockets
            : pockets.Where(pocket => pocket.Id == _selectedBagPocketId).ToArray();
        foreach (var pocket in visiblePockets)
        {
            if (_selectedBagPocketId is null)
            {
                _content.Children.Add(new Label
                {
                    Text = pocket.Name,
                    FontFamily = DsChrome.PixelFont,
                    FontSize = 10,
                    TextColor = UiTokens.Cyan,
                });
            }

            foreach (var item in pocket.Items)
            {
                var name = new Label
                {
                    Text = item.Name,
                    FontFamily = DsChrome.PixelFont,
                    FontSize = 10,
                    TextColor = UiTokens.Ink0,
                    LineBreakMode = LineBreakMode.TailTruncation,
                    VerticalTextAlignment = TextAlignment.Center,
                };
                var count = new Label
                {
                    Text = item.Count.ToString(),
                    FontFamily = DsChrome.PixelFont,
                    FontSize = 10,
                    TextColor = UiTokens.Cyan,
                    VerticalTextAlignment = TextAlignment.Center,
                };
                var row = new Grid
                {
                    ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)],
                    ColumnSpacing = 8,
                    Children = { name, count },
                };
                Grid.SetColumn(count, 1);
                _content.Children.Add(new Border
                {
                    Padding = new Thickness(8, 5),
                    BackgroundColor = UiTokens.Paper,
                    Stroke = UiTokens.ShellEdge,
                    StrokeThickness = 1,
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 5 },
                    Content = row,
                });
            }
        }
    }

    private void RenderSession()
    {
        if (_live is not { } live)
        {
            AddNote(AppStrings.CompanionNoSavedData);
            return;
        }

        AddNote(AppStrings.CompanionSavedDataNotice);

        AddValue(AppStrings.CompanionPartyLabel(live.PartyCount));
        AddValue(AppStrings.CompanionPlaytime(live.PlaytimeHours, live.PlaytimeMinutes));
        AddValue(string.IsNullOrWhiteSpace(live.GameCode) ? live.GameLabel : $"{live.GameLabel} · {live.GameCode}");
    }

    private void RenderRestorePoints()
    {
        var points = _companion?.RestorePoints ?? [];
        if (points.Count == 0)
        {
            AddNote(AppStrings.CompanionNoRestorePoints);
            return;
        }

        foreach (var point in points.Take(8))
            AddValue($"· {point.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}");
    }

    private void RenderSettings()
    {
        _content.Children.Add(new Label
        {
            Text = AppStrings.CompanionSettingsTitle,
            FontFamily = DsChrome.PixelFont,
            FontSize = 12,
            TextColor = UiTokens.Cyan,
            HorizontalTextAlignment = TextAlignment.Center,
        });
        _content.Children.Add(new Label
        {
            Text = AppStrings.CompanionSettingsHelp,
            FontFamily = DsChrome.PixelFont,
            FontSize = 9,
            TextColor = UiTokens.InkSoft,
        });

        if (IPlatformApplication.Current?.Services.GetService<GameCompanionStore>() is { SpeedControlAvailable: true } speedStore)
        {
            _content.Children.Add(new Label
            {
                Text = AppStrings.GameSpeed,
                FontFamily = DsChrome.PixelFont,
                FontSize = 10,
                TextColor = UiTokens.Ink0,
            });
            var speedRow = new Grid
            {
                ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star), new(GridLength.Star), new(GridLength.Star)],
                ColumnSpacing = 4,
            };
            for (var multiplier = 1; multiplier <= 4; multiplier++)
            {
                var selectedMultiplier = multiplier;
                var selected = multiplier == speedStore.SpeedMultiplier;
                var button = SmallButton(AppStrings.GameSpeedChoice(multiplier));
                button.TextColor = selected ? UiTokens.Cyan : UiTokens.Ink0;
                button.BackgroundColor = selected ? UiTokens.SelectFill : UiTokens.Paper;
                button.Clicked += (_, _) => speedStore.RequestSpeedMultiplier(selectedMultiplier);
                speedRow.Add(button, multiplier - 1, 0);
            }
            _content.Children.Add(speedRow);
            AddNote(AppStrings.GameSpeedHelp);
        }

        var visible = GameCompanionPreferences.VisibleTabs;
        foreach (var tab in Enum.GetValues<GameCompanionTab>())
        {
            var row = new Grid
            {
                ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto)],
                ColumnSpacing = 4,
                Children =
                {
                    new Label
                    {
                        Text = LabelFor(tab),
                        FontFamily = DsChrome.PixelFont,
                        FontSize = 10,
                        TextColor = UiTokens.Ink0,
                        VerticalTextAlignment = TextAlignment.Center,
                    },
                },
            };
            var toggle = SmallButton(visible.Contains(tab) ? AppStrings.CompanionHideTab : AppStrings.CompanionShowTab);
            toggle.IsEnabled = !visible.Contains(tab) || visible.Count > 1;
            toggle.Clicked += (_, _) =>
            {
                GameCompanionPreferences.ToggleTab(tab);
                if (!GameCompanionPreferences.VisibleTabs.Contains(_selectedTab))
                    _selectedTab = GameCompanionPreferences.HomeTab;
                Render();
            };
            var home = SmallButton(GameCompanionPreferences.HomeTab == tab ? "✓" : "⌂");
            home.IsEnabled = visible.Contains(tab);
            home.Clicked += (_, _) =>
            {
                GameCompanionPreferences.SetHomeTab(tab);
                _selectedTab = tab;
                _showSettings = false;
                Render();
            };
            row.Add(toggle, 1, 0);
            row.Add(home, 2, 0);
            _content.Children.Add(row);
        }
        _content.Children.Add(new Label
        {
            Text = $"{AppStrings.CompanionCurrentHome} · {LabelFor(GameCompanionPreferences.HomeTab)}",
            FontFamily = DsChrome.PixelFont,
            FontSize = 9,
            TextColor = UiTokens.Cyan,
        });
    }

    private void AddNote(string text) => _content.Children.Add(new Label
    {
        Text = text,
        FontFamily = DsChrome.PixelFont,
        FontSize = 10,
        TextColor = UiTokens.InkSoft,
        HorizontalTextAlignment = TextAlignment.Center,
    });

    private void AddValue(string text) => _content.Children.Add(new Label
    {
        Text = text,
        FontFamily = DsChrome.PixelFont,
        FontSize = 10,
        TextColor = UiTokens.Ink1,
        HorizontalTextAlignment = TextAlignment.Center,
    });

    private static string LabelFor(GameCompanionTab tab) => tab switch
    {
        GameCompanionTab.Party => AppStrings.CompanionParty,
        GameCompanionTab.Bag => AppStrings.CompanionBag,
        GameCompanionTab.Pokedex => AppStrings.CompanionPokedex,
        GameCompanionTab.Session => AppStrings.CompanionSession,
        GameCompanionTab.RestorePoints => AppStrings.CompanionRestorePoints,
        _ => tab.ToString().ToUpperInvariant(),
    };

    private static Button NavigationButton(string text, bool selected) => new()
    {
        Text = text,
        FontFamily = DsChrome.PixelFont,
        FontSize = 9,
        TextColor = selected ? UiTokens.Cyan : UiTokens.InkSoft,
        BackgroundColor = selected ? UiTokens.Paper : Colors.Transparent,
        BorderColor = UiTokens.ShellEdge,
        BorderWidth = 1,
        CornerRadius = 4,
        Padding = new Thickness(4, 3),
        MinimumHeightRequest = 34,
    };

    private static Button SmallButton(string text) => new()
    {
        Text = text,
        FontFamily = DsChrome.PixelFont,
        FontSize = 8,
        TextColor = UiTokens.Ink0,
        BackgroundColor = UiTokens.Paper,
        BorderColor = UiTokens.ShellEdge,
        BorderWidth = 1,
        CornerRadius = 4,
        Padding = new Thickness(6, 3),
        MinimumHeightRequest = 30,
    };
}
