using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Client.Services.Fishing;

internal readonly record struct BellonaDebugBox(double X, double Y, double Width, double Height, Color Color);

internal static class BellonaDebugOverlayService
{
    private static readonly bool Enabled =
        string.Equals(Environment.GetEnvironmentVariable("BELLONA_DEBUG_OVERLAY"), "1", StringComparison.Ordinal);

    private static BellonaDebugOverlayWindow? _window;

    public static void Update(IReadOnlyList<BellonaDebugBox> boxes)
    {
        if (!Enabled)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_window is null)
            {
                _window = new BellonaDebugOverlayWindow();
                _window.Show();
            }

            _window.SetBoxes(boxes);
        });
    }

    public static void Hide()
    {
        if (!Enabled)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_window is null)
            {
                return;
            }

            _window.Close();
            _window = null;
        });
    }
}

internal sealed class BellonaDebugOverlayWindow : Window
{
    private readonly BellonaDebugOverlayCanvas _canvas;

    public BellonaDebugOverlayWindow()
    {
        WindowDecorations = WindowDecorations.None;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        IsHitTestVisible = false;
        Background = Brushes.Transparent;
        Opacity = 1.0;

        var screens = Screens.Primary;
        var bounds = screens?.Bounds ?? new PixelRect(0, 0, 1920, 1080);
        Position = new PixelPoint(bounds.X, bounds.Y);
        Width = bounds.Width;
        Height = bounds.Height;

        _canvas = new BellonaDebugOverlayCanvas();
        Content = _canvas;
    }

    public void SetBoxes(IReadOnlyList<BellonaDebugBox> boxes)
    {
        _canvas.SetBoxes(boxes);
    }
}

internal sealed class BellonaDebugOverlayCanvas : Control
{
    private IReadOnlyList<BellonaDebugBox> _boxes = Array.Empty<BellonaDebugBox>();

    public void SetBoxes(IReadOnlyList<BellonaDebugBox> boxes)
    {
        _boxes = boxes;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        foreach (var box in _boxes)
        {
            if (box.Width <= 0 || box.Height <= 0)
            {
                continue;
            }

            var pen = new Pen(new SolidColorBrush(box.Color), 2);
            var x1 = box.X;
            var y1 = box.Y;
            var x2 = box.X + box.Width;
            var y2 = box.Y + box.Height;
            context.DrawLine(pen, new Point(x1, y1), new Point(x2, y1));
            context.DrawLine(pen, new Point(x2, y1), new Point(x2, y2));
            context.DrawLine(pen, new Point(x2, y2), new Point(x1, y2));
            context.DrawLine(pen, new Point(x1, y2), new Point(x1, y1));
        }
    }
}
