using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Layout;

namespace Umbrella.Wallet.App.Controls;

/// <summary>Which Phobia logo a <see cref="BrandMark"/> draws.</summary>
public enum BrandKind
{
    /// <summary>The two crystals — the app icon's own artwork.</summary>
    Mark,

    /// <summary>The crystals with the PHOBIA name under them.</summary>
    Wordmark,

    /// <summary>The crystals on their own (kept for layouts that asked for the old umbrella glyph).</summary>
    Glyph,

    /// <summary>The crystals on their own (kept for layouts that asked for the old canopy).</summary>
    Canopy,
}

/// <summary>
/// The Phobia logo in the colours of whatever theme is on — the logo's own blues on the default theme,
/// the theme's hue on any other (see <see cref="CrystalLogo"/>). Vector, so it is sharp at every size.
/// </summary>
public sealed class BrandMark : Panel
{
    public static readonly StyledProperty<bool> GlowProperty =
        AvaloniaProperty.Register<BrandMark, bool>(nameof(Glow), defaultValue: true);

    public static readonly StyledProperty<BrandKind> KindProperty =
        AvaloniaProperty.Register<BrandMark, BrandKind>(nameof(Kind), defaultValue: BrandKind.Mark);

    private readonly DropShadowEffect _glow = new() { OffsetX = 0, OffsetY = 0, BlurRadius = 22, Opacity = 0.5 };

    public BrandMark()
    {
        _glow.Bind(DropShadowEffect.ColorProperty, this.GetResourceObservable("UmGlow"));
        Effect = _glow;
        IsHitTestVisible = false;
        Build();
    }

    /// <summary>The accent glow behind the logo. Off where it sits on a busy surface or in a tile.</summary>
    public bool Glow
    {
        get => GetValue(GlowProperty);
        set => SetValue(GlowProperty, value);
    }

    public BrandKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == GlowProperty) Effect = Glow ? _glow : null;
        else if (change.Property == KindProperty) Build();
    }

    private void Build()
    {
        Children.Clear();
        if (Kind != BrandKind.Wordmark)
        {
            Children.Add(new CrystalLogo());
            return;
        }

        // The name set under the crystals, scaled with them as one piece.
        var name = new TextBlock
        {
            Text = "PHOBIA",
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
            LetterSpacing = 5,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(5, 8, 0, 0),
        };
        name.Bind(TextBlock.ForegroundProperty, name.GetResourceObservable("UmText"));
        Children.Add(new Viewbox
        {
            Stretch = Stretch.Uniform,
            Child = new StackPanel
            {
                Children = { new CrystalLogo { Width = 96, Height = 96, HorizontalAlignment = HorizontalAlignment.Center }, name },
            },
        });
    }
}
