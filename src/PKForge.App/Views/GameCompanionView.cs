using PKForge.App.Services;
using PKForge.App.Theme;
using PKForge.Domain;

namespace PKForge.App.Views;

/// <summary>
/// The in-game companion on the lower screen: the running game's name and dex
/// progress, plus its restore points. Shown while an emulator owns the
/// foreground; every surface is read-only (the companion never edits a save the
/// emulator holds open).
/// </summary>
public sealed class GameCompanionView : VerticalStackLayout
{
    private readonly Label _title = new()
    {
        FontFamily = DsChrome.PixelFont, FontSize = 16, TextColor = UiTokens.Ink0,
        HorizontalTextAlignment = TextAlignment.Center,
    };
    private readonly Label _dex = new()
    {
        FontFamily = DsChrome.PixelFont, FontSize = 13, TextColor = UiTokens.Cyan,
        HorizontalTextAlignment = TextAlignment.Center,
    };
    private readonly VerticalStackLayout _points = new() { Spacing = 3 };

    public GameCompanionView()
    {
        Spacing = 10;
        Margin = new Thickness(14, 12);
        Padding = new Thickness(10);
        HorizontalOptions = LayoutOptions.Center;
        VerticalOptions = LayoutOptions.Center;
        Children.Add(_title);
        Children.Add(_dex);
        Children.Add(new Label
        {
            Text = AppStrings.Fr ? "Points de restauration" : "Restore points",
            FontFamily = DsChrome.PixelFont, FontSize = 11, TextColor = UiTokens.Ink1,
        });
        Children.Add(_points);
        IsVisible = false;
    }

    /// <summary>Replaces the companion wholesale: one immutable record per launch.</summary>
    public void Show(GameCompanion companion)
    {
        _title.Text = companion.GameLabel;
        _dex.Text = AppStrings.Fr
            ? $"Pokédex : {companion.DexCaught}/{companion.DexTotal} capturés · {companion.DexPercent}%"
            : $"Pokédex: {companion.DexCaught}/{companion.DexTotal} caught · {companion.DexPercent}%";
        _points.Children.Clear();
        if (companion.RestorePoints.Count == 0)
        {
            _points.Children.Add(new Label
            {
                Text = AppStrings.Fr ? "Aucun point pour cette sauvegarde." : "No points for this save yet.",
                FontFamily = DsChrome.PixelFont, FontSize = 10, TextColor = UiTokens.InkSoft,
            });
        }
        else
        {
            foreach (var point in companion.RestorePoints.Take(5))
            {
                _points.Children.Add(new Label
                {
                    Text = $"· {point.CreatedUtc:yyyy-MM-dd HH:mm} UTC",
                    FontFamily = DsChrome.PixelFont, FontSize = 10, TextColor = UiTokens.InkSoft,
                });
            }
        }
        IsVisible = true;
    }
}
