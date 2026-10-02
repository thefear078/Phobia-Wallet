using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Umbrella.Wallet.App.Controls;

/// <summary>
/// A line of light (the gold design): a series drawn as a smooth curve in the theme's accent, with a
/// soft glow under it, a wash of the accent fading down to the baseline, a lit dot where it ends and
/// a small label beside that dot. Sizes itself to whatever space it is given.
///
/// It draws what it is handed and nothing else — no smoothing of the data, no invented points. The
/// curve passes through every value (see <see cref="ChartGeometry"/>).
///
/// Under the pointer it follows the line: a crosshair, a lit dot on the curve and the value of that
/// very point with its caption (<see cref="PointLabels"/>, <see cref="PointCaptions"/>), redrawn on
/// every move — nothing is fetched or recomputed while the pointer travels.
/// </summary>
public sealed class GlowChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<GlowChart, IReadOnlyList<double>?>(nameof(Values));

    public static readonly StyledProperty<Color> LineColorProperty =
        AvaloniaProperty.Register<GlowChart, Color>(nameof(LineColor), Colors.Gold);

    public static readonly StyledProperty<string?> EndLabelProperty =
        AvaloniaProperty.Register<GlowChart, string?>(nameof(EndLabel));

    public static readonly StyledProperty<string?> EndCaptionProperty =
        AvaloniaProperty.Register<GlowChart, string?>(nameof(EndCaption));

    public static readonly StyledProperty<IBrush?> LabelBackgroundProperty =
        AvaloniaProperty.Register<GlowChart, IBrush?>(nameof(LabelBackground));

    public static readonly StyledProperty<IBrush?> LabelBorderProperty =
        AvaloniaProperty.Register<GlowChart, IBrush?>(nameof(LabelBorder));

    public static readonly StyledProperty<IBrush?> LabelForegroundProperty =
        AvaloniaProperty.Register<GlowChart, IBrush?>(nameof(LabelForeground));

    public static readonly StyledProperty<IBrush?> CaptionForegroundProperty =
        AvaloniaProperty.Register<GlowChart, IBrush?>(nameof(CaptionForeground));

    /// <summary>What the box says at each point while the pointer is over it, one per value.</summary>
    public static readonly StyledProperty<IReadOnlyList<string>?> PointLabelsProperty =
        AvaloniaProperty.Register<GlowChart, IReadOnlyList<string>?>(nameof(PointLabels));

    /// <summary>The smaller line under each point's label — when it was, and the move since the start.</summary>
    public static readonly StyledProperty<IReadOnlyList<string>?> PointCaptionsProperty =
        AvaloniaProperty.Register<GlowChart, IReadOnlyList<string>?>(nameof(PointCaptions));

    /// <summary>
    /// The least height the scale may stand for, as a share of the values. Zero lets a series fill the
    /// chart however little it moves; the balance chart sets a floor, so a wallet of stablecoins that
    /// moved by 0.05% draws as the near-flat line it is, not as a mountain range.
    /// </summary>
    public static readonly StyledProperty<double> MinSpanFractionProperty =
        AvaloniaProperty.Register<GlowChart, double>(nameof(MinSpanFraction));

    private static readonly Typeface LabelFace = new(new FontFamily("Segoe UI Variable, Segoe UI"), FontStyle.Normal, FontWeight.SemiBold);
    private static readonly Typeface CaptionFace = new(new FontFamily("Segoe UI Variable, Segoe UI"));

    static GlowChart()
    {
        AffectsRender<GlowChart>(ValuesProperty, LineColorProperty, EndLabelProperty, EndCaptionProperty,
            LabelBackgroundProperty, LabelBorderProperty, LabelForegroundProperty, CaptionForegroundProperty,
            PointLabelsProperty, PointCaptionsProperty, MinSpanFractionProperty);
        ValuesProperty.Changed.AddClassHandler<GlowChart>((chart, _) => chart._hover = -1);
    }

    public GlowChart() => Cursor = new Cursor(StandardCursorType.Cross);

    public IReadOnlyList<double>? Values { get => GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public Color LineColor { get => GetValue(LineColorProperty); set => SetValue(LineColorProperty, value); }
    public string? EndLabel { get => GetValue(EndLabelProperty); set => SetValue(EndLabelProperty, value); }
    public string? EndCaption { get => GetValue(EndCaptionProperty); set => SetValue(EndCaptionProperty, value); }
    public IBrush? LabelBackground { get => GetValue(LabelBackgroundProperty); set => SetValue(LabelBackgroundProperty, value); }
    public IBrush? LabelBorder { get => GetValue(LabelBorderProperty); set => SetValue(LabelBorderProperty, value); }
    public IBrush? LabelForeground { get => GetValue(LabelForegroundProperty); set => SetValue(LabelForegroundProperty, value); }
    public IBrush? CaptionForeground { get => GetValue(CaptionForegroundProperty); set => SetValue(CaptionForegroundProperty, value); }
    public IReadOnlyList<string>? PointLabels { get => GetValue(PointLabelsProperty); set => SetValue(PointLabelsProperty, value); }
    public IReadOnlyList<string>? PointCaptions { get => GetValue(PointCaptionsProperty); set => SetValue(PointCaptionsProperty, value); }
    public double MinSpanFraction { get => GetValue(MinSpanFractionProperty); set => SetValue(MinSpanFractionProperty, value); }

    private const double PadLeft = 4, PadRight = 14, PadTop = 14, PadBottom = 6;

    /// <summary>The point under the pointer, or -1.</summary>
    private int _hover = -1;

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Follow(e.GetPosition(this).X);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Follow(e.GetPosition(this).X);   // a touch screen has no hover: a tap shows the point
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hover < 0) return;
        _hover = -1;
        InvalidateVisual();
    }

    private void Follow(double x)
    {
        var count = Values?.Count ?? 0;
        if (count < 2) return;
        var plotW = Bounds.Width - PadLeft - PadRight;
        if (plotW <= 0) return;
        var index = NearestIndex(x - PadLeft, plotW, count);
        if (index == _hover) return;
        _hover = index;
        InvalidateVisual();
    }

    /// <summary>The point nearest to <paramref name="x"/> across a plot <paramref name="width"/> wide.</summary>
    public static int NearestIndex(double x, double width, int count) =>
        count < 2 || width <= 0 ? 0 : Math.Clamp((int)Math.Round(x / width * (count - 1)), 0, count - 1);

    /// <summary>The bottom and top of the scale: the series' own, widened around its middle to at
    /// least <paramref name="minSpanFraction"/> of its size.</summary>
    public static (double Min, double Max) Scale(IReadOnlyList<double> values, double minSpanFraction)
    {
        var min = double.MaxValue;
        var max = double.MinValue;
        foreach (var v in values)
        {
            min = Math.Min(min, v);
            max = Math.Max(max, v);
        }

        var floor = Math.Max(Math.Abs(min), Math.Abs(max)) * Math.Max(0, minSpanFraction);
        if (max - min < floor)
        {
            var mid = (max + min) / 2;
            min = mid - floor / 2;
            max = mid + floor / 2;
        }
        return (min, max);
    }

    public override void Render(DrawingContext ctx)
    {
        var values = Values;
        if (values is null || values.Count < 2) return;

        var w = Bounds.Width;
        var h = Bounds.Height;
        const double padLeft = PadLeft, padRight = PadRight, padTop = PadTop, padBottom = PadBottom;
        if (w <= padLeft + padRight || h <= padTop + padBottom) return;

        // Transparent, not nothing: the whole card follows the pointer, not just the pixels of the line.
        ctx.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

        var (min, max) = Scale(values, MinSpanFraction);

        // A flat series (a wallet of stablecoins) is a flat line through the middle, not a divide by zero.
        var range = max - min;
        var flat = range <= Math.Abs(max) * 1e-9 || range <= 0;

        var points = new List<Point>(values.Count);
        var plotW = w - padLeft - padRight;
        var plotH = h - padTop - padBottom;
        for (var i = 0; i < values.Count; i++)
        {
            var x = padLeft + plotW * i / (values.Count - 1);
            var y = flat ? padTop + plotH * 0.5 : padTop + (1 - (values[i] - min) / range) * plotH;
            points.Add(new Point(x, y));
        }

        var c = LineColor;
        Color A(byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

        // The wash under the line.
        var area = ChartGeometry.Area(points, h);
        if (area is not null)
        {
            var fill = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(A(0x50), 0),
                    new GradientStop(A(0x14), 0.6),
                    new GradientStop(A(0x00), 1),
                },
            };
            ctx.DrawGeometry(fill, null, area);
        }

        // The line: two soft wide strokes for the glow, then the bright one.
        var line = ChartGeometry.Line(points);
        if (line is not null)
        {
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(A(0x14)), 14, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), line);
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(A(0x2E)), 6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), line);
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(c), 2.4, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), line);
        }

        // Where it ends — now.
        var end = points[^1];
        var hover = _hover >= 0 && _hover < points.Count ? _hover : -1;
        ctx.DrawEllipse(new SolidColorBrush(A(hover >= 0 ? (byte)0x1C : (byte)0x38)), null, end, 11, 11);
        ctx.DrawEllipse(new SolidColorBrush(c), new Pen(LabelBackground ?? Brushes.Black, 2), end, 5.5, 5.5);

        if (hover >= 0)
        {
            // The crosshair: a faint rule down the card and a lit dot on the curve itself.
            var at = points[hover];
            var rule = new Pen(new SolidColorBrush(A(0x66)), 1, new DashStyle([3, 3], 0));
            ctx.DrawLine(rule, new Point(at.X, padTop - 6), new Point(at.X, h - padBottom));
            ctx.DrawEllipse(new SolidColorBrush(A(0x40)), null, at, 10, 10);
            ctx.DrawEllipse(new SolidColorBrush(c), new Pen(LabelBackground ?? Brushes.Black, 2), at, 5, 5);

            var text = PointLabels is { } labels && hover < labels.Count
                ? labels[hover]
                : values[hover].ToString("N2", CultureInfo.CurrentCulture);
            var sub = PointCaptions is { } captions && hover < captions.Count ? captions[hover] : null;
            DrawBox(ctx, text, sub, at, w, h, beside: false);
            return;
        }

        if (string.IsNullOrEmpty(EndLabel)) return;
        DrawBox(ctx, EndLabel, EndCaption, end, w, h, beside: true);
    }

    /// <summary>The label box: beside the end dot, or centred over a hovered point.</summary>
    private void DrawBox(DrawingContext ctx, string text, string? sub, Point at, double w, double h, bool beside)
    {
        var label = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelFace, 13,
            LabelForeground ?? Brushes.White);
        FormattedText? caption = string.IsNullOrEmpty(sub)
            ? null
            : new FormattedText(sub, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, CaptionFace, 10.5,
                CaptionForeground ?? Brushes.Gray);

        var boxW = Math.Max(label.Width, caption?.Width ?? 0) + 22;
        var boxH = label.Height + (caption?.Height ?? 0) + 14;
        var boxX = Math.Clamp(beside ? at.X - boxW - 16 : at.X - boxW / 2, 0, Math.Max(0, w - boxW));
        var boxY = at.Y - boxH - 12;
        if (boxY < 0) boxY = Math.Min(h - boxH, at.Y + 14);

        var box = new Rect(boxX, boxY, boxW, boxH);
        ctx.DrawRectangle(LabelBackground, LabelBorder is null ? null : new Pen(LabelBorder, 1), box, 9, 9);
        ctx.DrawText(label, new Point(boxX + 11, boxY + 7));
        if (caption is not null) ctx.DrawText(caption, new Point(boxX + 11, boxY + 7 + label.Height));
    }
}
