using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.Threading;

namespace Umbrella.Wallet.App.Controls;

/// <summary>Which of Phobia's ambient effects a <see cref="CrystalEffects"/> layer plays.</summary>
public enum CrystalEffectKind
{
    /// <summary>Loose crystals rising slowly through the page and turning a little as they go.</summary>
    Floating,

    /// <summary>Tiny facets of light that rise a little and twinkle out.</summary>
    Glints,
}

/// <summary>
/// Phobia's ambient motion, built to cost almost nothing. Any animation makes the whole window redraw on
/// every frame it runs — at 60 frames a second that measured at more than half a CPU core with the wallet
/// sitting idle, whether the animation was a style or a compositor one. So these move at 12 frames a
/// second (they drift a few pixels a second; the eye cannot tell) and stop entirely whenever the window is
/// not the one in front: an idle wallet in the background costs nothing.
/// </summary>
public sealed class CrystalEffects : Canvas
{
    public static readonly StyledProperty<CrystalEffectKind> KindProperty =
        AvaloniaProperty.Register<CrystalEffects, CrystalEffectKind>(nameof(Kind));

    public static readonly StyledProperty<bool> ActiveProperty =
        AvaloniaProperty.Register<CrystalEffects, bool>(nameof(Active));

    private const double FramesPerSecond = 12;

    private readonly List<Mover> _movers = [];
    private readonly DispatcherTimer _timer;
    private readonly DateTime _started = DateTime.UtcNow;
    private Window? _window;
    private Size _builtFor;

    public CrystalEffects()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1 / FramesPerSecond), DispatcherPriority.Background, (_, _) => Step());
    }

    public CrystalEffectKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>Bound to the settings switch (and the master motion switch). Off empties the layer.</summary>
    public bool Active
    {
        get => GetValue(ActiveProperty);
        set => SetValue(ActiveProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ActiveProperty || change.Property == KindProperty) Rebuild();
        else if (change.Property == BoundsProperty && Bounds.Size != _builtFor) Rebuild();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is not null)
        {
            _window.Activated += OnWindowActivity;
            _window.Deactivated += OnWindowActivity;
        }
        UpdateTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_window is not null)
        {
            _window.Activated -= OnWindowActivity;
            _window.Deactivated -= OnWindowActivity;
            _window = null;
        }
        _timer.Stop();
    }

    private void OnWindowActivity(object? sender, EventArgs e) => UpdateTimer();

    private void UpdateTimer()
    {
        var run = _movers.Count > 0 && (_window?.IsActive ?? false);
        if (run && !_timer.IsEnabled) _timer.Start();
        else if (!run && _timer.IsEnabled) _timer.Stop();
    }

    /// <summary>One moving item: its transform, how long a loop takes, where in the loop it starts, how far
    /// it rises and drifts, its brightest opacity, how far it turns, and whether it twinkles mid-way.</summary>
    private sealed record Mover(Control Item, TranslateTransform Move, RotateTransform Turn, double Seconds,
        double Phase, double Rise, double Drift, double Peak, double Degrees, bool Twinkle);

    private void Rebuild()
    {
        Children.Clear();
        _movers.Clear();
        _builtFor = Bounds.Size;
        if (Active && Bounds.Width >= 10 && Bounds.Height >= 10)
        {
            var rng = new Random(Kind == CrystalEffectKind.Floating ? 17 : 29);
            double Next(double min, double max) => min + (rng.NextDouble() * (max - min));

            var count = Kind == CrystalEffectKind.Floating ? 8 : 10;
            for (var i = 0; i < count; i++)
            {
                Control item;
                double seconds, rise, drift, peak, degrees;
                bool twinkle;
                if (Kind == CrystalEffectKind.Floating)
                {
                    var w = Next(12, 26);
                    item = new CrystalShard { Width = w, Height = w * 1.9 };
                    (seconds, rise, drift, peak, degrees, twinkle) = (Next(18, 32), Next(160, 300), Next(-30, 30), Next(0.18, 0.32), Next(-30, 30), false);
                    SetTop(item, Next(0.35, 1.0) * Bounds.Height);
                }
                else
                {
                    var glint = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M4,0 L8,4 L4,8 L0,4 Z"), Width = 8, Height = 8 };
                    glint.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, glint.GetResourceObservable(i % 3 == 1 ? "UmAccent2" : "UmAccentHover"));
                    item = glint;
                    (seconds, rise, drift, peak, degrees, twinkle) = (Next(5.5, 10.5), 70, 0, Next(0.4, 0.6), 0, true);
                    SetTop(item, Next(0.05, 0.95) * Bounds.Height);
                }

                SetLeft(item, Next(0.02, 0.98) * Bounds.Width);
                var move = new TranslateTransform();
                var turn = new RotateTransform();
                item.RenderTransform = new TransformGroup { Children = { turn, move } };
                item.Opacity = 0;
                Children.Add(item);
                _movers.Add(new Mover(item, move, turn, seconds, rng.NextDouble(), rise, drift, peak, degrees, twinkle));
            }
        }
        UpdateTimer();
    }

    private void Step()
    {
        if (!IsEffectivelyVisible) return;
        var t = (DateTime.UtcNow - _started).TotalSeconds;
        foreach (var m in _movers)
        {
            var p = ((t / m.Seconds) + m.Phase) % 1;
            m.Move.Y = -m.Rise * p;
            m.Move.X = m.Drift * p;
            m.Turn.Angle = m.Degrees * p;
            m.Item.Opacity = m.Peak * Envelope(p, m.Twinkle);
        }
    }

    /// <summary>Fades in, holds, fades out — and a twinkle dims to a third half-way through.</summary>
    private static double Envelope(double p, bool twinkle)
    {
        if (twinkle)
        {
            return p switch
            {
                < 0.3 => p / 0.3,
                < 0.55 => 1 - ((p - 0.3) / 0.25 * 0.7),
                < 0.72 => 0.3 + ((p - 0.55) / 0.17 * 0.7),
                _ => 1 - ((p - 0.72) / 0.28),
            };
        }
        return p switch
        {
            < 0.18 => p / 0.18,
            < 0.82 => 1,
            _ => 1 - ((p - 0.82) / 0.18),
        };
    }
}

/// <summary>
/// The balance card's shine: every nine seconds a soft skewed band of light crosses the card once, played
/// by the compositor — frames are drawn only during the 1.4-second sweep, never while it rests, and not at
/// all while the window is in the background.
/// </summary>
public sealed class ShineSweep : Canvas
{
    public static readonly StyledProperty<bool> ActiveProperty =
        AvaloniaProperty.Register<ShineSweep, bool>(nameof(Active));

    private readonly DispatcherTimer _timer;
    private Border? _band;

    public ShineSweep()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(9), DispatcherPriority.Background, (_, _) => Sweep());
    }

    public bool Active
    {
        get => GetValue(ActiveProperty);
        set => SetValue(ActiveProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ActiveProperty || (change.Property == BoundsProperty && _band is not null)) Rebuild();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Rebuild();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer.Stop();
    }

    private void Rebuild()
    {
        Children.Clear();
        _band = null;
        _timer.Stop();
        if (!Active || Bounds.Width < 10) return;

        _band = new Border
        {
            Width = 170,
            Height = Bounds.Height * 1.6,
            RenderTransform = new SkewTransform(-18, 0),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0),
                    new GradientStop(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF), 0.5),
                    new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1),
                },
            },
        };
        SetLeft(_band, -260);
        SetTop(_band, -Bounds.Height * 0.3);
        Children.Add(_band);
        _timer.Start();
    }

    private void Sweep()
    {
        if (_band is null || !IsEffectivelyVisible || TopLevel.GetTopLevel(this) is Window { IsActive: false }) return;
        if (ElementComposition.GetElementVisual(_band) is not { } visual) return;

        var start = new Vector3D(-260, visual.Offset.Y, 0);
        var sweep = visual.Compositor.CreateVector3DKeyFrameAnimation();
        sweep.InsertKeyFrame(0f, start);
        sweep.InsertKeyFrame(1f, new Vector3D(Bounds.Width + 200, start.Y, 0));
        sweep.Duration = TimeSpan.FromSeconds(1.4);
        sweep.IterationBehavior = AnimationIterationBehavior.Count;
        sweep.IterationCount = 1;
        visual.StartAnimation("Offset", sweep);
    }
}
