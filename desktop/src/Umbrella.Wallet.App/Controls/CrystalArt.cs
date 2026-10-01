using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

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

    /// <summary>
    /// Moves a colour of the artwork (drawn around <paramref name="artworkHue"/>) onto <paramref name="hue"/>,
    /// keeping a third of its own hue offset — so the crystal's blue-to-violet shading survives as a gentle
    /// shift within the new colour instead of swinging into a neighbouring one (violet facets turned lime on
    /// the gold theme when the whole spread was kept).
    /// </summary>
    public static Color Rehue(Color colour, double artworkHue, double hue, double saturationScale)
    {
        var (h, s, l) = ToHsl(colour);
        var offset = ((h - artworkHue + 540) % 360) - 180;
        var (r, g, b) = FromHsl((((hue + (offset * 0.35)) % 360) + 360) % 360, Math.Clamp(s * saturationScale, 0, 1), l);
        return Color.FromRgb(r, g, b);
    }

    public static (double H, double S, double L) Hsl(Color c) => ToHsl(c);

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

/// <summary>
/// The one piece of scenery Phobia keeps: crystalline mountains along the bottom of the screens where the
/// wallet is locked or being set up — the Phobia artwork pared down to a single bold shape. Two ranges (a
/// far, faint one behind a near one), each cut into facets shaded by the way they face, a thin line of the
/// accent along the near crest, and a faint glow of it rising behind. Colours come from the theme's accent
/// (<c>UmGlow</c>), mixed toward black, so every theme gets its own mountains. Deterministic and still.
/// </summary>
public sealed class CrystalRidge : Control
{
    public static readonly StyledProperty<Color> AccentProperty =
        AvaloniaProperty.Register<CrystalRidge, Color>(nameof(Accent), Color.Parse("#5B3FE8"));

    static CrystalRidge() => AffectsRender<CrystalRidge>(AccentProperty);

    private static readonly Color Night = Color.Parse("#07060C");

    public CrystalRidge()
    {
        Bind(AccentProperty, this.GetResourceObservable("UmGlow"));
        IsHitTestVisible = false;
    }

    public Color Accent
    {
        get => GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        // The glow the mountains stand in front of.
        var glow = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        };
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x30, Accent.R, Accent.G, Accent.B), 0));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, Accent.R, Accent.G, Accent.B), 1));
        context.FillRectangle(glow, new Rect(0, 0, w, h));

        Range(context, w, h, seed: 11, peaks: 9, low: 0.30, high: 0.92, strength: 0.55, crest: false);
        Range(context, w, h, seed: 4, peaks: 13, low: 0.12, high: 0.58, strength: 1.0, crest: true);
    }

    private void Range(DrawingContext context, double w, double h, int seed, int peaks,
        double low, double high, double strength, bool crest)
    {
        var rng = new Random(seed);
        var points = new List<Point>();
        for (var i = 0; i <= peaks; i++)
        {
            var x = (-0.04 + (1.08 * i / peaks) + ((rng.NextDouble() - 0.5) * 0.04)) * w;
            // Alternate summits and saddles, so the line reads as a range rather than noise.
            var lift = i % 2 == 0 ? low + ((high - low) * (0.55 + (0.45 * rng.NextDouble())))
                                  : low + ((high - low) * 0.35 * rng.NextDouble());
            points.Add(new Point(x, h - (lift * h)));
        }

        Color Shade(double k) => Color.FromArgb(
            (byte)Math.Round(255 * strength),
            (byte)Math.Round(Night.R + ((Accent.R - Night.R) * k)),
            (byte)Math.Round(Night.G + ((Accent.G - Night.G) * k)),
            (byte)Math.Round(Night.B + ((Accent.B - Night.B) * k)));

        // The body of the range, then its facets: every slope split at a foot point below its middle, the
        // side that faces the light (up and to the left) a step brighter than the side that faces away.
        var body = new List<Point>(points) { new(w + 2, h + 2), new(-2, h + 2) };
        context.DrawGeometry(new SolidColorBrush(Shade(0.14)), null, CrystalLogo.Polygon(body));
        for (var i = 0; i < points.Count - 1; i++)
        {
            var a = points[i];
            var b = points[i + 1];
            var foot = new Point((a.X + b.X) / 2 + ((rng.NextDouble() - 0.5) * 0.02 * w), h + 2);
            var rising = b.Y < a.Y;
            context.DrawGeometry(new SolidColorBrush(Shade(rising ? 0.30 : 0.10)), null, CrystalLogo.Polygon([a, b, foot]));
            // A narrow sliver of light on the summit's lit side.
            var summit = rising ? b : a;
            var sliver = new Point(summit.X + (rising ? -0.012 * w : 0.012 * w), summit.Y + (0.22 * (h - summit.Y)));
            context.DrawGeometry(new SolidColorBrush(Shade(0.42)), null, CrystalLogo.Polygon([summit, sliver, foot]));
        }

        if (!crest) return;
        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(points[0], isFilled: false);
            for (var i = 1; i < points.Count; i++) ctx.LineTo(points[i]);
            ctx.EndFigure(isClosed: false);
        }
        context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(0x8C, Accent.R, Accent.G, Accent.B)), 1.2), line);
    }
}

/// <summary>
/// One loose crystal — the floating crystals behind the page are made of these. A small control of its own
/// (not one big canvas) so each moves under its own animation and only its own few pixels are redrawn.
/// Four facets in the crystal palette, re-hued with the theme like the logo.
/// </summary>
public sealed class CrystalShard : Control
{
    public static readonly StyledProperty<Color> TintProperty =
        AvaloniaProperty.Register<CrystalShard, Color>(nameof(Tint), Colors.Transparent);

    static CrystalShard() => AffectsRender<CrystalShard>(TintProperty);

    public CrystalShard()
    {
        Bind(TintProperty, this.GetResourceObservable("UmCrystalTint"));
        IsHitTestVisible = false;
    }

    public Color Tint
    {
        get => GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        // Long axis vertical: tips top and bottom, shoulders off-centre, a ridge where the faces meet.
        var top = new Point(w * 0.46, 0);
        var bottom = new Point(w * 0.54, h);
        var right = new Point(w, h * 0.38);
        var left = new Point(0, h * 0.56);
        var ridge = new Point(w * 0.56, h * 0.44);

        void Face(Point a, Point b, Color from, Color to)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(a, RelativeUnit.Absolute),
                EndPoint = new RelativePoint(ridge, RelativeUnit.Absolute),
            };
            brush.GradientStops.Add(new GradientStop(CrystalPalette.Tinted(from, Tint), 0));
            brush.GradientStops.Add(new GradientStop(CrystalPalette.Tinted(to, Tint), 1));
            context.DrawGeometry(brush, null, CrystalLogo.Polygon([a, b, ridge]));
        }

        context.DrawGeometry(new SolidColorBrush(CrystalPalette.Tinted(CrystalPalette.Shade, Tint)), null,
            CrystalLogo.Polygon([top, right, bottom, left]));
        Face(top, right, CrystalPalette.Highlight, CrystalPalette.Light);
        Face(right, bottom, CrystalPalette.Side, CrystalPalette.Shade);
        Face(bottom, left, CrystalPalette.Front, CrystalPalette.Deep);
        Face(left, top, CrystalPalette.Mid, CrystalPalette.Front);
    }
}

/// <summary>
/// The secondary Phobia logo: the single large cut crystal, for the places the brand is an illustration
/// rather than a signature — the launch screen, the balance card, the promo cards. Drawn from its artwork
/// in its own blues on the Phobia theme, and re-hued to any other theme's accent (every colour turned by
/// the same angle, so its blue-to-violet shading survives), cached per colour.
/// </summary>
public sealed class CrystalGem : Image
{
    public static readonly StyledProperty<Color> TintProperty =
        AvaloniaProperty.Register<CrystalGem, Color>(nameof(Tint), Colors.Transparent);

    private const string Artwork = "avares://Umbrella.Wallet.App/Assets/phobia-crystal.png";

    /// <summary>The hue the artwork is drawn around (its main blue), which the theme's hue replaces.</summary>
    private const double ArtworkHue = 222;

    private static Bitmap? _artwork;
    private static readonly Dictionary<uint, Bitmap> Tinted = [];

    public CrystalGem()
    {
        Stretch = Stretch.Uniform;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
        IsHitTestVisible = false;
        Bind(TintProperty, this.GetResourceObservable("UmCrystalTint"));
        Source = For(Tint);
    }

    public Color Tint
    {
        get => GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TintProperty) Source = For(Tint);
    }

    private static Bitmap For(Color tint)
    {
        _artwork ??= new Bitmap(AssetLoader.Open(new Uri(Artwork)));
        if (tint.A == 0) return _artwork;
        var key = tint.ToUInt32();
        if (Tinted.TryGetValue(key, out var cached)) return cached;
        var made = Recolour(_artwork, tint);
        Tinted[key] = made;
        return made;
    }

    private static WriteableBitmap Recolour(Bitmap source, Color tint)
    {
        var target = new WriteableBitmap(source.PixelSize, source.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var frame = target.Lock();
        source.CopyPixels(frame, AlphaFormat.Premul);

        var bytes = new byte[frame.RowBytes * frame.Size.Height];
        Marshal.Copy(frame.Address, bytes, 0, bytes.Length);

        var (tintHue, tintSat) = HueAndSaturation(tint);
        var satScale = tintSat / 0.8;
        for (var i = 0; i < bytes.Length; i += 4)
        {
            var a = bytes[i + 3];
            if (a == 0) continue;
            // Premultiplied BGRA: undo the alpha, turn the hue, put it back.
            var k = 255.0 / a;
            var c = Color.FromRgb(
                (byte)Math.Min(255, bytes[i + 2] * k), (byte)Math.Min(255, bytes[i + 1] * k), (byte)Math.Min(255, bytes[i] * k));
            var turned = CrystalPalette.Rehue(c, ArtworkHue, tintHue, satScale);
            bytes[i] = (byte)(turned.B * a / 255);
            bytes[i + 1] = (byte)(turned.G * a / 255);
            bytes[i + 2] = (byte)(turned.R * a / 255);
        }

        Marshal.Copy(bytes, 0, frame.Address, bytes.Length);
        return target;
    }

    private static (double Hue, double Saturation) HueAndSaturation(Color c)
    {
        var (h, s, _) = CrystalPalette.Hsl(c);
        return (h, s);
    }
}
