using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Umbrella.Wallet.App.Controls;

/// <summary>Which small picture an <see cref="Illustration"/> draws.</summary>
public enum IllustrationKind
{
    /// <summary>A shield with a keyhole — keys that stay yours ("your crypto, under your control").</summary>
    Vault,

    /// <summary>A rising line with its glow and a few candles — the market.</summary>
    Market,

    /// <summary>A quiet dashed line — a chart that has nothing to show yet.</summary>
    EmptyChart,
}

/// <summary>
/// Small vector pictures for the places a logo used to be repeated: each says what its card is about
/// instead of saying "Phobia" again. Drawn in the theme's accent (<c>UmGlow</c>) and the crystal blue, so
/// they belong to whichever theme is on; sharp at any size, no image files.
/// </summary>
public sealed class Illustration : Control
{
    public static readonly StyledProperty<IllustrationKind> KindProperty =
        AvaloniaProperty.Register<Illustration, IllustrationKind>(nameof(Kind));

    public static readonly StyledProperty<Color> AccentProperty =
        AvaloniaProperty.Register<Illustration, Color>(nameof(Accent), Color.Parse("#5B3FE8"));

    private static readonly Color Blue = Color.Parse("#4E7BFF");

    static Illustration() => AffectsRender<Illustration>(KindProperty, AccentProperty);

    public Illustration()
    {
        Bind(AccentProperty, this.GetResourceObservable("UmGlow"));
        IsHitTestVisible = false;
    }

    public IllustrationKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public Color Accent
    {
        get => GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    private Color A(byte alpha) => Color.FromArgb(alpha, Accent.R, Accent.G, Accent.B);

    private Color Light(byte alpha) => Color.FromArgb(alpha,
        (byte)((Accent.R + 255) / 2), (byte)((Accent.G + 255) / 2), (byte)((Accent.B + 255) / 2));

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        switch (Kind)
        {
            case IllustrationKind.Vault:
                Vault(context, w, h);
                break;
            case IllustrationKind.Market:
                Market(context, w, h);
                break;
            default:
                EmptyChart(context, w, h);
                break;
        }
    }

    private void Glow(DrawingContext context, Point centre, double radius, byte alpha)
    {
        var glow = new RadialGradientBrush();
        glow.GradientStops.Add(new GradientStop(A(alpha), 0));
        glow.GradientStops.Add(new GradientStop(A(0), 1));
        context.DrawEllipse(glow, null, centre, radius, radius);
    }

    private void Vault(DrawingContext context, double w, double h)
    {
        var size = Math.Min(w, h);
        var ox = (w - size) / 2;
        var oy = (h - size) / 2;
        Point P(double x, double y) => new(ox + (x / 24 * size), oy + (y / 24 * size));

        Glow(context, P(12, 12), size * 0.55, 0x55);

        var shield = new StreamGeometry();
        using (var g = shield.Open())
        {
            g.BeginFigure(P(12, 2.5), true);
            g.LineTo(P(20.5, 5.8));
            g.LineTo(P(20.5, 11.8));
            g.CubicBezierTo(P(20.5, 16.6), P(16.8, 20.1), P(12, 21.5));
            g.CubicBezierTo(P(7.2, 20.1), P(3.5, 16.6), P(3.5, 11.8));
            g.LineTo(P(3.5, 5.8));
            g.EndFigure(true);
        }
        var fill = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        };
        fill.GradientStops.Add(new GradientStop(Color.FromArgb(0x70, Blue.R, Blue.G, Blue.B), 0));
        fill.GradientStops.Add(new GradientStop(A(0x50), 1));
        context.DrawGeometry(fill, new Pen(new SolidColorBrush(Light(0xE0)), Math.Max(1.2, size * 0.022)), shield);

        // The keyhole.
        var hole = new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF));
        context.DrawEllipse(hole, null, P(12, 10.4), size * 0.075, size * 0.075);
        context.DrawGeometry(hole, null, CrystalLogo.Polygon([P(11.1, 11), P(12.9, 11), P(13.4, 15.6), P(10.6, 15.6)]));

        // Two facets of light beside it.
        foreach (var (x, y, r) in new[] { (21.8, 3.2, 0.9), (2.4, 16.8, 0.65) })
        {
            var c = P(x, y);
            var d = r / 24 * size;
            context.DrawGeometry(new SolidColorBrush(Light(0xC0)), null,
                CrystalLogo.Polygon([new(c.X, c.Y - d), new(c.X + d, c.Y), new(c.X, c.Y + d), new(c.X - d, c.Y)]));
        }
    }

    private void Market(DrawingContext context, double w, double h)
    {
        // Faint rules.
        var rule = new Pen(new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF)), 1);
        for (var i = 1; i <= 3; i++) context.DrawLine(rule, new Point(0, h * i / 4), new Point(w, h * i / 4));

        // Candles along the bottom half.
        double[] tops = [0.72, 0.64, 0.68, 0.55, 0.58, 0.46];
        for (var i = 0; i < tops.Length; i++)
        {
            var x = w * (0.08 + (i * 0.15));
            var bar = new Rect(x, h * tops[i], w * 0.05, h * (0.9 - tops[i]));
            context.FillRectangle(new SolidColorBrush(A(i % 2 == 0 ? (byte)0x55 : (byte)0x33)), bar, 2);
        }

        // The rising line, its glow and the area under it.
        Point[] pts = [new(0, h * 0.72), new(w * 0.22, h * 0.6), new(w * 0.42, h * 0.66), new(w * 0.62, h * 0.4), new(w * 0.8, h * 0.44), new(w, h * 0.14)];
        var line = new StreamGeometry();
        var area = new StreamGeometry();
        using (var g = line.Open())
        using (var a = area.Open())
        {
            g.BeginFigure(pts[0], false);
            a.BeginFigure(new Point(0, h), true);
            a.LineTo(pts[0]);
            for (var i = 1; i < pts.Length; i++)
            {
                var c1 = new Point((pts[i - 1].X + pts[i].X) / 2, pts[i - 1].Y);
                var c2 = new Point((pts[i - 1].X + pts[i].X) / 2, pts[i].Y);
                g.CubicBezierTo(c1, c2, pts[i]);
                a.CubicBezierTo(c1, c2, pts[i]);
            }
            g.EndFigure(false);
            a.LineTo(new Point(w, h));
            a.EndFigure(true);
        }
        var under = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        };
        under.GradientStops.Add(new GradientStop(A(0x50), 0));
        under.GradientStops.Add(new GradientStop(A(0x00), 1));
        context.DrawGeometry(under, null, area);

        var stroke = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
        };
        stroke.GradientStops.Add(new GradientStop(A(0xFF), 0));
        stroke.GradientStops.Add(new GradientStop(Blue, 1));
        context.DrawGeometry(null, new Pen(stroke, 2.4, lineCap: PenLineCap.Round), line);

        var end = pts[^1];
        Glow(context, end, Math.Min(w, h) * 0.22, 0x90);
        context.DrawEllipse(Brushes.White, null, new Point(end.X - 2, end.Y + 1), 3.2, 3.2);
    }

    private void EmptyChart(DrawingContext context, double w, double h)
    {
        // A gentle dashed wave across the middle: there is a chart here, it just has nothing to draw yet.
        var wave = new StreamGeometry();
        using (var g = wave.Open())
        {
            g.BeginFigure(new Point(0, h * 0.62), false);
            g.CubicBezierTo(new Point(w * 0.25, h * 0.42), new Point(w * 0.4, h * 0.78), new Point(w * 0.6, h * 0.55));
            g.CubicBezierTo(new Point(w * 0.78, h * 0.36), new Point(w * 0.88, h * 0.5), new Point(w, h * 0.4));
            g.EndFigure(false);
        }
        var pen = new Pen(new SolidColorBrush(A(0x70)), 1.6, new DashStyle([4, 4], 0), PenLineCap.Round);
        context.DrawGeometry(null, pen, wave);
        context.DrawEllipse(new SolidColorBrush(A(0xC0)), null, new Point(w, h * 0.4), 3, 3);
    }
}
