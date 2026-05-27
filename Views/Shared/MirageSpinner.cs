using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using SkiaSharp;

namespace AvaloniaSpinners;

/// <summary>
/// "Mirage" loading indicator — five dots that stream across with a gooey
/// merging effect. Port of the CSS/SVG version.
/// </summary>
public class MirageSpinner : Control
{
    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<MirageSpinner, double>(nameof(Size), 60.0);

    public static readonly StyledProperty<Color> ColorProperty =
        AvaloniaProperty.Register<MirageSpinner, Color>(nameof(Color), Colors.Black);

    public static readonly StyledProperty<TimeSpan> SpeedProperty =
        AvaloniaProperty.Register<MirageSpinner, TimeSpan>(
            nameof(Speed), TimeSpan.FromSeconds(2.6));

    private readonly Stopwatch _stopwatch = new();
    private DispatcherTimer? _timer;

    static MirageSpinner()
    {
        AffectsMeasure<MirageSpinner>(SizeProperty);
        AffectsRender<MirageSpinner>(ColorProperty);
    }

    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public Color Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public TimeSpan Speed
    {
        get => GetValue(SpeedProperty);
        set => SetValue(SpeedProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _stopwatch.Restart();
        _timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(16),
            DispatcherPriority.Render,
            (_, _) => InvalidateVisual());
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer?.Stop();
        _timer = null;
        _stopwatch.Stop();
    }

    protected override Avalonia.Size MeasureOverride(Avalonia.Size availableSize)
    {
        var s = Size;
        return new Avalonia.Size(s, s * 0.23);
    }

    public override void Render(DrawingContext context)
    {
        var s = Size;
        var dot = s * 0.23;
        var speed = Speed.TotalSeconds <= 0 ? 2.6 : Speed.TotalSeconds;

        context.Custom(new MirageDrawOp(
            new Avalonia.Rect(0, 0, s, dot),
            _stopwatch.Elapsed.TotalSeconds,
            speed, s, dot, Color));
    }

    private sealed class MirageDrawOp : ICustomDrawOperation
    {
        private readonly Avalonia.Rect _bounds;
        private readonly double _t, _speed, _size, _dot;
        private readonly Color _color;

        public MirageDrawOp(Avalonia.Rect bounds, double t, double speed,
                            double size, double dot, Color color)
        {
            _bounds = bounds;
            _t = t; _speed = speed; _size = size; _dot = dot; _color = color;
        }

        public Avalonia.Rect Bounds => _bounds;
        public bool HitTest(Avalonia.Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature is null) return;

            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            var sk = new SKColor(_color.R, _color.G, _color.B, _color.A);

            // ---- Filter chain matching the SVG <filter id="uib-jelly-ooze"> ----
            // 1) feGaussianBlur stdDeviation="3"
            // 2) feColorMatrix on alpha: a' = 18*a - 7  (binary-threshold the blur)
            using var blur = SKImageFilter.CreateBlur(3f, 3f);
            var matrix = new float[]
            {
                1, 0, 0, 0,  0,
                0, 1, 0, 0,  0,
                0, 0, 1, 0,  0,
                0, 0, 0, 18, -7,
            };
            using var ooze = SKImageFilter.CreateColorFilter(
                SKColorFilter.CreateColorMatrix(matrix), blur);

            using var oozePaint = new SKPaint { ImageFilter = ooze };
            using var dotPaint = new SKPaint { Color = sk, IsAntialias = true };

            // Pad the layer so blur isn't clipped near edges.
            var pad = (float)Math.Max(_dot, 20);
            var layerBounds = new SKRect(
                -pad, -pad, (float)_size + pad, (float)_dot + pad);

            // Pass 1: gooey blob (the "ooze" output)
            canvas.SaveLayer(layerBounds, oozePaint);
            DrawDots(canvas, dotPaint);
            canvas.Restore();

            // Pass 2: crisp source on top — corresponds to <feBlend in="SourceGraphic" in2="ooze"/>
            DrawDots(canvas, dotPaint);
        }

        private void DrawDots(SKCanvas canvas, SKPaint paint)
        {
            for (int i = 0; i < 5; i++)
            {
                // CSS used negative animation-delays of -0.2*speed per dot.
                var phase = ((_t / _speed) + i * 0.2) % 1.0;

                double x, scale;
                if (phase < 0.5)
                {
                    var p = phase / 0.5;          // 0 -> 1
                    x = p * _size * 0.5;
                    scale = p;
                }
                else
                {
                    var p = (phase - 0.5) / 0.5;  // 0 -> 1
                    x = _size * 0.5 + p * _size * 0.5;
                    scale = 1.0 - p;
                }

                var r = (float)(_dot * scale * 0.5);
                if (r > 0.01f)
                    canvas.DrawCircle((float)x, (float)(_dot / 2), r, paint);
            }
        }
    }
}
