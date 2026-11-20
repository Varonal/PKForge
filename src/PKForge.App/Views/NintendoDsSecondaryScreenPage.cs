using PKForge.App.Platforms.Android;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace PKForge.App.Views;

/// <summary>Displays the Nintendo DS lower framebuffer on Android's secondary display.</summary>
public sealed class NintendoDsSecondaryScreenPage : ContentPage
{
    private readonly IEmbeddedGameSession _session;
    private readonly SKCanvasView _canvas = new() { IgnorePixelScaling = true, EnableTouchEvents = true };
    private SKRect _screenRect;
    private long _touchPointerId = -1;
    private bool _cleanedUp;

    public IEmbeddedGameSession Session => _session;

    public NintendoDsSecondaryScreenPage(IEmbeddedGameSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        BackgroundColor = Colors.Black;
        Content = _canvas;
        _canvas.PaintSurface += OnPaint;
        _canvas.Touch += OnTouch;
        _session.FrameProduced += Invalidate;
    }

    private void OnPaint(object? sender, SKPaintSurfaceEventArgs args)
    {
        var canvas = args.Surface.Canvas;
        canvas.Clear(SKColors.Black);
        var scale = Math.Min(args.Info.Width / 256f, args.Info.Height / 192f);
        if (scale <= 0) return;

        var width = 256 * scale;
        var height = 192 * scale;
        var left = (args.Info.Width - width) / 2f;
        var top = (args.Info.Height - height) / 2f;
        _screenRect = new SKRect(left, top, left + width, top + height);
        _session.DrawSecondaryScreen(canvas, _screenRect);
    }

    private void OnTouch(object? sender, SKTouchEventArgs args)
    {
        switch (args.ActionType)
        {
            case SKTouchAction.Pressed when _touchPointerId == -1:
                _touchPointerId = args.Id;
                UpdateTouch(args.Location);
                args.Handled = true;
                break;
            case SKTouchAction.Moved when args.Id == _touchPointerId:
                UpdateTouch(args.Location);
                args.Handled = true;
                break;
            case SKTouchAction.Released or SKTouchAction.Cancelled when args.Id == _touchPointerId:
                ReleaseTouch();
                args.Handled = true;
                break;
        }
    }

    private void UpdateTouch(SKPoint location)
    {
        if (!_screenRect.Contains(location.X, location.Y) || _screenRect.Width <= 0 || _screenRect.Height <= 0)
        {
            _session.SetTouchScreen(false, 0, 0);
            return;
        }

        var x = (int)((location.X - _screenRect.Left) * 256f / _screenRect.Width);
        var y = (int)((location.Y - _screenRect.Top) * 192f / _screenRect.Height);
        _session.SetTouchScreen(true, Math.Clamp(x, 0, 255), Math.Clamp(y, 0, 191));
    }

    private void ReleaseTouch()
    {
        _touchPointerId = -1;
        _session.SetTouchScreen(false, 0, 0);
    }

    private void Invalidate() => MainThread.InvokeOnMainThreadAsync(_canvas.InvalidateSurface);

    public void Cleanup()
    {
        if (_cleanedUp) return;
        _cleanedUp = true;
        ReleaseTouch();
        _session.FrameProduced -= Invalidate;
        _canvas.PaintSurface -= OnPaint;
        _canvas.Touch -= OnTouch;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        Cleanup();
    }
}
