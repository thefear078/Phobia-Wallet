using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Umbrella.Wallet.App.Controls;

/// <summary>
/// The colours of Phobia's crystals, and how they follow a theme. The logo's facets are drawn in their
/// own blues on the Phobia theme; on any other theme each facet keeps its lightness (so the cut still
/// reads) and takes the theme accent's hue — gold crystals on the gold theme, grey on monochrome.
/// </summary>
internal static class CrystalPalette
{
    // Sampled from the logo artwork, lightest to deepest.
    public static readonly Color Highlight = Color.Parse("#B8E8FF");
    public static readonly Color Light = Color.Parse("#A6DCFF");
    public static readonly Color Side = Color.Parse("#7CC9FF");
    public static readonly Color Bright = Color.Parse("#62B6FF");
    public static readonly Color Mid = Color.Parse("#559FF4");
    public static readonly Color Shade = Color.Parse("#4A88DD");
    public static readonly Color Front = Color.Parse("#316EE5");
    public static readonly Color Low = Color.Parse("#2E62D0");
    public static readonly Color Deep = Color.Parse("#2B50B1");

    /// <summary>The colour as drawn (a transparent tint) or re-hued to the tint's colour.</summary>
    public static Color Tinted(Color colour, Color tint, byte alpha = 255)
    {
        if (tint.A == 0) return Color.FromArgb(alpha, colour.R, colour.G, colour.B);

        var (_, s, l) = ToHsl(colour);
        var (th, ts, _) = ToHsl(tint);
        // The logo's blues are ~0.8 saturated; scale so a muted accent gives muted crystals.
        var saturation = Math.Clamp(s * ts / 0.8, 0, 1);
        var (r, g, b) = FromHsl(th, saturation, l);
        return Color.FromArgb(alpha, r, g, b);
    }

    private static (double H, double S, double L) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (max - min < 1e-9) return (0, 0, l);

        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h;
        if (max == r) h = ((g - b) / d) + (g < b ? 6 : 0);
        else if (max == g) h = ((b - r) / d) + 2;
        else h = ((r - g) / d) + 4;
        return (h * 60, s, l);
    }

    private static (byte R, byte G, byte B) FromHsl(double h, double s, double l)
    {
        if (s <= 0)
        {
            var v = (byte)Math.Round(l * 255);
            return (v, v, v);
        }

        var q = l < 0.5 ? l * (1 + s) : l + s - (l * s);
        var p = (2 * l) - q;
        var hk = h / 360;

        static double Channel(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + ((q - p) * 6 * t);
            if (t < 0.5) return q;
            if (t < 2.0 / 3) return p + ((q - p) * ((2.0 / 3) - t) * 6);
            return p;
        }

        return ((byte)Math.Round(Channel(p, q, hk + (1.0 / 3)) * 255),
                (byte)Math.Round(Channel(p, q, hk) * 255),
                (byte)Math.Round(Channel(p, q, hk - (1.0 / 3)) * 255));
    }
}

/// <summary>
/// The Phobia logo — two cut crystals — drawn as vectors, so it is sharp at 16 px in a title bar and
/// at 512 px in an installer. Traced facet by facet from the logo artwork, on its 96×96 grid.
/// </summary>
public sealed class CrystalLogo : Control
{
    public static readonly StyledProperty<Color> TintProperty =
        AvaloniaProperty.Register<CrystalLogo, Color>(nameof(Tint), Colors.Transparent);

    static CrystalLogo() => AffectsRender<CrystalLogo>(TintProperty);

    public CrystalLogo()
    {
        Bind(TintProperty, this.GetResourceObservable("UmCrystalTint"));
        IsHitTestVisible = false;
    }

    /// <summary>Transparent draws the logo's own blues; any other colour re-hues it (see
    /// <see cref="CrystalPalette.Tinted"/>). Bound to the theme's <c>UmCrystalTint</c>.</summary>
    public Color Tint
    {
        get => GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }

    private sealed record Facet(Point[] Points, Color From, Color To, Point Start, Point End);

    private static Point P(double x, double y) => new(x, y);

    // Left crystal: T top, L left point, R ridge, S right shoulder, RB lower right, B bottom tip,
    // I inner foot of the ridge, J where the right face's two bands meet the foot.
    private static readonly Point T = P(29.5, 5), L = P(6, 38), R = P(40, 28), S = P(52.5, 31),
        RB = P(53, 88), B = P(45.5, 95.5), I = P(44.5, 91), J = P(47.5, 86), K = P(44.5, 30.2);

    // Right crystal: T2 top, L2 lower left, R2 right point, B2 bottom, K1/K2 the ridge.
    private static readonly Point T2 = P(61, 0.5), L2 = P(60, 43), R2 = P(89, 40), B2 = P(74, 59.5),
        K1 = P(64.5, 36), K2 = P(71, 36.5);

    private static readonly Point[] LeftOutline = [T, S, RB, B, L];
    private static readonly Point[] RightOutline = [T2, R2, B2, L2];

    private static readonly Facet[] Facets =
    [
        // Left crystal.
        new([T, R, L], CrystalPalette.Mid, CrystalPalette.Shade, P(28, 8), P(10, 36)),
        new([T, S, R], CrystalPalette.Light, CrystalPalette.Highlight, P(30, 6), P(50, 30)),
        new([L, R, I, B], CrystalPalette.Front, CrystalPalette.Deep, P(36, 30), P(14, 60)),
        new([R, K, J, I], CrystalPalette.Bright, Color.Parse("#66BCFF"), P(41, 30), P(46, 88)),
        new([K, S, RB, J], CrystalPalette.Light, Color.Parse("#86CCFF"), P(48, 32), P(50, 86)),
        new([J, RB, B, I], CrystalPalette.Mid, CrystalPalette.Shade, P(50, 86), P(45, 95)),
        // Right crystal.
        new([T2, K1, L2], CrystalPalette.Bright, CrystalPalette.Shade, P(61, 4), P(61, 42)),
        new([T2, R2, K2, K1], CrystalPalette.Light, CrystalPalette.Highlight, P(62, 4), P(86, 38)),
        new([L2, K1, K2, B2], CrystalPalette.Low, CrystalPalette.Deep, P(64, 37), P(72, 58)),
        new([K2, R2, B2], CrystalPalette.Side, CrystalPalette.Shade, P(84, 40), P(74, 58)),
    ];

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;

        var scale = size / 96.0;
        var offset = new Point((Bounds.Width - size) / 2, (Bounds.Height - size) / 2);
        using var _ = context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset.X, offset.Y));

        // The silhouette underneath in the middle blue, so no background shows through the seams where
        // two facets meet; its tips softly rounded, like the artwork's, and the facets cut to it.
        var under = new SolidColorBrush(CrystalPalette.Tinted(CrystalPalette.Shade, Tint));
        var outline = new GeometryGroup
        {
            Children = { Rounded(LeftOutline, 1.8), Rounded(RightOutline, 1.6) },
        };
        context.DrawGeometry(under, null, outline);
        using var clip = context.PushGeometryClip(outline);

        foreach (var facet in Facets)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(facet.Start, RelativeUnit.Absolute),
                EndPoint = new RelativePoint(facet.End, RelativeUnit.Absolute),
            };
            brush.GradientStops.Add(new GradientStop(CrystalPalette.Tinted(facet.From, Tint), 0));
            brush.GradientStops.Add(new GradientStop(CrystalPalette.Tinted(facet.To, Tint), 1));
            context.DrawGeometry(brush, null, Polygon(facet.Points));
        }
    }

    /// <summary>A polygon whose corners are rounded by <paramref name="radius"/> (quadratic joins).</summary>
    private static StreamGeometry Rounded(IReadOnlyList<Point> points, double radius)
    {
        Point Toward(Point from, Point to)
        {
            var d = to - from;
            var len = Math.Sqrt((d.X * d.X) + (d.Y * d.Y));
            var t = Math.Min(radius / len, 0.45);
            return new Point(from.X + (d.X * t), from.Y + (d.Y * t));
        }

        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        var n = points.Count;
        ctx.BeginFigure(Toward(points[0], points[1]), isFilled: true);
        for (var i = 1; i <= n; i++)
        {
            var v = points[i % n];
            ctx.LineTo(Toward(v, points[(i - 1 + n) % n]));
            ctx.QuadraticBezierTo(v, Toward(v, points[(i + 1) % n]));
        }
        ctx.EndFigure(isClosed: true);
        return geometry;
    }

    internal static StreamGeometry Polygon(IReadOnlyList<Point> points)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        ctx.BeginFigure(points[0], isFilled: true);
        for (var i = 1; i < points.Count; i++) ctx.LineTo(points[i]);
        ctx.EndFigure(isClosed: true);
        return geometry;
    }
}

/// <summary>Where a <see cref="CrystalField"/> puts its crystals.</summary>
public enum CrystalLayout
{
    /// <summary>A drift across the lower half, like the Phobia artwork — for the welcome and lock screens.</summary>
    Band,

    /// <summary>A few in the top-right and bottom-left corners, clear of the content — behind the wallet.</summary>
    Corners,
}

/// <summary>
/// Crystals drifting in the dark: the background of the Phobia artwork, drawn as vectors so it is sharp
/// at any window size and follows the theme. Deterministic (a fixed seed), so the scene does not jump
/// between launches, and still — nothing here animates or takes input.
/// </summary>
public sealed class CrystalField : Control
{
    public static readonly StyledProperty<Color> TintProperty =
        AvaloniaProperty.Register<CrystalField, Color>(nameof(Tint), Colors.Transparent);

    public static readonly StyledProperty<CrystalLayout> LayoutProperty =
        AvaloniaProperty.Register<CrystalField, CrystalLayout>(nameof(Layout), CrystalLayout.Band);

    public static readonly StyledProperty<int> SeedProperty =
        AvaloniaProperty.Register<CrystalField, int>(nameof(Seed), 7);

    static CrystalField() => AffectsRender<CrystalField>(TintProperty, LayoutProperty, SeedProperty);

    public CrystalField()
    {
        Bind(TintProperty, this.GetResourceObservable("UmCrystalTint"));
        IsHitTestVisible = false;
    }

    public Color Tint
    {
        get => GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }

    public CrystalLayout Layout
    {
        get => GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    public int Seed
    {
        get => GetValue(SeedProperty);
        set => SetValue(SeedProperty, value);
    }

    /// <summary>One crystal: position (0..1 of the field), length (0..1 of its shorter side), slant,
    /// thickness, how far along its axis the ridge sits, and how far away it is (0 near, 1 far).</summary>
    private readonly record struct Shard(double X, double Y, double Length, double Angle, double Width, double Ridge, double Depth, bool Flare = false);

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        var unit = Math.Min(w, h);
        foreach (var shard in Shards(Layout, Seed))
        {
            DrawShard(context, shard, w, h, unit, Tint);
        }
    }

    private static IEnumerable<Shard> Shards(CrystalLayout layout, int seed)
    {
        var rng = new Random(seed);
        double Next(double min, double max) => min + (rng.NextDouble() * (max - min));

        if (layout == CrystalLayout.Corners)
        {
            // Two loose clusters where the corners are empty: top right and bottom left.
            foreach (var (cx, cy, spreadX, spreadY) in new[] { (0.9, 0.12, 0.14, 0.16), (0.1, 0.9, 0.16, 0.12) })
            {
                for (var i = 0; i < 9; i++)
                {
                    var depth = Next(0, 1);
                    yield return new Shard(
                        cx + Next(-spreadX, spreadX), cy + Next(-spreadY, spreadY),
                        Next(0.025, 0.09) * (1.3 - depth), Next(-1.1, 1.1), Next(0.32, 0.55), Next(0.3, 0.6), depth);
                }
            }
            yield break;
        }

        // The artwork's drift: most crystals in a band across the lower middle, larger and nearer toward
        // the lower left, small far ones scattered above them — and the middle column, where the screen's
        // own text and buttons are, left clear.
        for (var i = 0; i < 46; i++)
        {
            var x = Next(-0.02, 1.02);
            if (x is > 0.3 and < 0.7) x = x < 0.5 ? x - 0.3 : x + 0.3;
            var band = 0.62 - (0.18 * x);                    // the band rises gently to the right
            var y = band + (Next(-1, 1) * Next(0, 0.26));
            var depth = Math.Clamp(Next(0, 1) + (x * 0.25), 0, 1);
            var length = Next(0.02, 0.11) * (1.25 - depth);
            yield return new Shard(x, y, length, Next(-1.2, 1.2), Next(0.3, 0.6), Next(0.3, 0.65), depth, rng.NextDouble() < 0.2);
        }
    }

    private static void DrawShard(DrawingContext context, Shard s, double w, double h, double unit, Color tint)
    {
        var length = s.Length * unit;
        var half = length / 2;
        var width = length * s.Width;

        // A cut crystal: two tips on its long axis, two shoulders across it, and a ridge where the four
        // faces meet, off-centre so the light and dark sides differ in size.
        Point Rot(double px, double py)
        {
            var (sin, cos) = Math.SinCos(s.Angle);
            return new Point((s.X * w) + (px * cos) - (py * sin), (s.Y * h) + (px * sin) + (py * cos));
        }

        var top = Rot(0, -half);
        var bottom = Rot(0, half);
        var right = Rot(width / 2, -half * 0.15);
        var left = Rot(-width / 2, half * 0.1);
        var ridge = Rot(width * 0.08, (s.Ridge - 0.5) * length);

        // Far crystals are fainter; near ones carry the full cut.
        var alpha = (byte)Math.Round(255 * (0.95 - (0.6 * s.Depth)));
        var faces = new (Point[] Points, Color Colour)[]
        {
            ([top, right, ridge], CrystalPalette.Highlight),
            ([right, bottom, ridge], CrystalPalette.Side),
            ([bottom, left, ridge], CrystalPalette.Front),
            ([left, top, ridge], CrystalPalette.Mid),
        };

        var outline = CrystalLogo.Polygon([top, right, bottom, left]);
        context.DrawGeometry(new SolidColorBrush(CrystalPalette.Tinted(CrystalPalette.Shade, tint, alpha)), null, outline);
        foreach (var (points, colour) in faces)
        {
            // Each face darkens from its outer edge toward the ridge, so the cut reads as depth.
            var edge = new Point((points[0].X + points[1].X) / 2, (points[0].Y + points[1].Y) / 2);
            var face = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(edge, RelativeUnit.Absolute),
                EndPoint = new RelativePoint(points[2], RelativeUnit.Absolute),
            };
            face.GradientStops.Add(new GradientStop(CrystalPalette.Tinted(colour, tint, alpha), 0));
            face.GradientStops.Add(new GradientStop(CrystalPalette.Tinted(Blend(colour, CrystalPalette.Deep, 0.28), tint, alpha), 1));
            context.DrawGeometry(face, null, CrystalLogo.Polygon(points));
        }

        // Now and then light catches a tip, as in the artwork.
        if (s.Flare)
        {
            var radius = Math.Max(6, length * 0.45);
            var flare = new RadialGradientBrush();
            flare.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(alpha * 0.55), 255, 255, 255), 0));
            flare.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
            context.DrawEllipse(flare, null, top, radius, radius);
        }
    }

    private static Color Blend(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + ((b.R - a.R) * t)),
        (byte)Math.Round(a.G + ((b.G - a.G) * t)),
        (byte)Math.Round(a.B + ((b.B - a.B) * t)));
}
