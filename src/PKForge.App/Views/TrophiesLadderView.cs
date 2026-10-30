using PKForge.App.Services;
using PKForge.App.Theme;

namespace PKForge.App.Views;

/// <summary>
/// The lower screen's trophy surface: the game's name, its completion percent, and the
/// ladder rows (green unlocked, muted locked), drawn with the shared DS-era kit.
/// </summary>
public sealed class TrophiesLadderView : VerticalStackLayout
{
    private readonly Label _title = new()
    {
        FontFamily = DsChrome.PixelFont, FontSize = 16, TextColor = UiTokens.Ink0,
        HorizontalTextAlignment = TextAlignment.Center,
    };
    private readonly Label _percent = new()
    {
        FontFamily = DsChrome.PixelFont, FontSize = 13, TextColor = UiTokens.Cyan,
        HorizontalTextAlignment = TextAlignment.Center,
    };
    private readonly VerticalStackLayout _rows = new() { Spacing = 3 };

    public TrophiesLadderView()
    {
        Spacing = 8;
        Margin = new Thickness(14, 12);
        Padding = new Thickness(10);
        HorizontalOptions = LayoutOptions.Center;
        VerticalOptions = LayoutOptions.Center;
        Children.Add(_title);
        Children.Add(_percent);
        Children.Add(_rows);
        IsVisible = false;
    }

    /// <summary>Replaces the ladder wholesale: the payload is one immutable record per refresh.</summary>
    public void Show(TrophyLadder ladder)
    {
        _title.Text = ladder.GameLabel;
        _percent.Text = $"{ladder.Percent}% · {ladder.Unlocked}/{ladder.Total} TROPHIES";
        _rows.Children.Clear();
        foreach (var (title, glyph, unlocked, detail) in ladder.Rows)
        {
            var glyphLabel = new Label
            {
                Text = glyph, FontSize = 13, WidthRequest = 20,
                TextColor = unlocked ? UiTokens.Gold : UiTokens.Ink1, Opacity = unlocked ? 1.0 : 0.55,
                VerticalOptions = LayoutOptions.Center,
            };
            var titleLabel = new Label
            {
                Text = title, FontFamily = DsChrome.PixelFont, FontSize = 12,
                TextColor = unlocked ? UiTokens.Green : UiTokens.Ink1, LineBreakMode = LineBreakMode.TailTruncation,
                VerticalOptions = LayoutOptions.Center,
            };
            var detailLabel = new Label
            {
                Text = detail, FontFamily = DsChrome.PixelFont, FontSize = 10,
                TextColor = UiTokens.InkSoft, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1,
                HorizontalTextAlignment = TextAlignment.End, VerticalOptions = LayoutOptions.Center,
            };
            // The columns must be assigned: without them all three labels stack in
            // column 0 (the one-column bug).
            Grid.SetColumn(glyphLabel, 0);
            Grid.SetColumn(titleLabel, 1);
            Grid.SetColumn(detailLabel, 2);
            _rows.Children.Add(new Grid
            {
                ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)],
                ColumnSpacing = 8,
                Children = { glyphLabel, titleLabel, detailLabel },
            });
        }
        IsVisible = true;
    }
}
