using PKForge.App.Platforms.Android;
using PKForge.App.Services;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace PKForge.App.Views;

/// <summary>
/// The embedded game screen (option A): the mGBA core renders the game in-process
/// and this page blits it. Pixel-crisp: integer scaling only (a fractional scale
/// would smear the GBA's 240×160), letterboxed when the screen's ratio isn't 3:2.
/// </summary>
public sealed class EmbeddedGamePage : ContentPage
{
    private readonly SKCanvasView _canvas = new() { IgnorePixelScaling = true };
    private readonly EmbeddedGameSession _session;
    private readonly Dictionary<PadButton, uint> _held = [];
    private readonly object _heldGate = new();

    public EmbeddedGamePage(EmbeddedGameSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        BackgroundColor = Colors.Black;
        Content = _canvas;
        _canvas.PaintSurface += OnPaint;
        _session.FrameProduced += Invalidate;
    }

    private void Invalidate() => MainThread.InvokeOnMainThreadAsync(_canvas.InvalidateSurface);

    /// <summary>The pad's button → GBA bit mapping (mGBA's own bit order).</summary>
    private static uint BitOf(PadButton button) => button switch
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
            _session.SetKeys(_held.Values.Aggregate(0u, (mask, value) => mask | value));
        }
        return true; // the game screen consumes every button
    }

    /// <summary>IPadReleaseHandler: a release clears the bit (hold gestures work).</summary>
    public void OnPadReleased(PadButton button)
    {
        lock (_heldGate)
        {
            _held.Remove(button);
            _session.SetKeys(_held.Values.Aggregate(0u, (mask, value) => mask | value));
        }
    }

    private void OnPaint(object? sender, SKPaintSurfaceEventArgs args)
    {
        var canvas = args.Surface.Canvas;
        canvas.Clear(SKColors.Black);
        var (width, height) = _session.VideoSize;
        if (width == 0 || height == 0) return;

        // Integer scale, centered: the GBA's pixels stay square and unsmeared.
        var scale = Math.Min(
            (float)Math.Floor((double)args.Info.Width / width),
            (float)Math.Floor((double)args.Info.Height / height));
        if (scale < 1) scale = Math.Min((float)args.Info.Width / width, (float)args.Info.Height / height);

        var drawWidth = width * scale;
        var drawHeight = height * scale;
        var left = (args.Info.Width - drawWidth) / 2f;
        var top = (args.Info.Height - drawHeight) / 2f;
        _session.Draw(canvas, new SKRect(left, top, left + drawWidth, top + drawHeight));
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        IPlatformApplication.Current?.Services.GetService<GamepadRouter>()?.Push(this);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _session.FrameProduced -= Invalidate;
        IPlatformApplication.Current?.Services.GetService<GamepadRouter>()?.Remove(this);
        _session.Dispose();
    }
}
