using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;

namespace Umbrella.Wallet.App;

/// <summary>
/// Runtime colour themes.
///
/// Every themed surface binds a DynamicResource key, so swapping the palette repaints the whole
/// window without a restart. The QR code's white plate is deliberately NOT themed — a QR needs
/// dark-on-light contrast to scan, and tinting it would quietly break receiving.
/// </summary>
public static class Theming
{
    public sealed record ThemeOption(string Id, string Name);

    public static IReadOnlyList<ThemeOption> Themes { get; } =
    [
        new("purple", "Purple · the fear"),
        new("blue", "Blue"),
        new("green", "Green"),
        new("black", "Black (OLED)"),
        new("white", "White (light)"),
        new("gradient", "Gradient · violet"),
        new("sunset", "Gradient · sunset"),
        new("crimson", "Crimson"),
        new("amber", "Amber"),
        new("slate", "Slate"),
    ];

    /// <summary>Order matters only for readability; every theme must define every key.</summary>
    private static readonly string[] Keys =
    [
        "UmBg", "UmBgAlt", "UmCard", "UmInput", "UmCardAlt", "UmHover",
        "UmBorder", "UmBorder2", "UmBorder3",
        "UmAccent", "UmAccentBright", "UmAccentHover", "UmAccentSel", "UmAccentDim",
        "UmText", "UmTextSoft", "UmTextDim", "UmTextMuted", "UmPos",
        "UmInverse", "UmInverseHover", "UmInverseText",
    ];

    private static readonly Dictionary<string, string[]> Palettes = new()
    {
        // bg       bgAlt    card     input    cardAlt  hover    bd       bd2      bd3      accent   accentBr accentHv accentSel accentDim text    textSoft textDim  textMut  pos
        ["purple"] =
        [
            "#0C0D10", "#12111C", "#1A1730", "#181425", "#241F45", "#2A2350",
            "#282442", "#332C5C", "#3A3168",
            "#4B3F86", "#6E5FB8", "#5B4CA8", "#3B2F73", "#3B3468",
            "#F4F5F7", "#C9CDD4", "#8B909A", "#7E8590", "#7DCF8F",
            "#F4F5F7", "#FFFFFF", "#0C0D10",
        ],
        ["blue"] =
        [
            "#0A0E16", "#101725", "#141C2E", "#121927", "#1B2740", "#223354",
            "#1E2A42", "#2A3A58", "#33456A",
            "#2F5C9E", "#4A83D6", "#3C6FBF", "#274E86", "#26405F",
            "#F2F5FA", "#C3CDDB", "#8792A5", "#6F7A8C", "#5AC8B4",
            "#F2F5FA", "#FFFFFF", "#0A0E16",
        ],
        ["green"] =
        [
            "#08110C", "#0E1A13", "#12211A", "#101E17", "#1A2E22", "#22402F",
            "#1C2E23", "#294436", "#325240",
            "#2F7A4E", "#4FB877", "#429B63", "#276542", "#27503A",
            "#F1F8F3", "#C2D6C8", "#86998C", "#6E8175", "#7DCF8F",
            "#F1F8F3", "#FFFFFF", "#08110C",
        ],
        ["black"] =
        [
            "#000000", "#070707", "#0D0D0D", "#0A0A0A", "#141414", "#1E1E1E",
            "#1A1A1A", "#262626", "#333333",
            "#3A3A3A", "#8A8A8A", "#4A4A4A", "#2A2A2A", "#222222",
            "#FFFFFF", "#CFCFCF", "#9A9A9A", "#7A7A7A", "#7DCF8F",
            "#FFFFFF", "#E8E8E8", "#000000",
        ],
        // Light theme: text roles invert so contrast survives the flip.
        ["white"] =
        [
            "#F4F5F7", "#FFFFFF", "#FFFFFF", "#F0F1F4", "#E9EBF0", "#DFE3EC",
            "#DDE0E7", "#CBD0DA", "#B8BFCC",
            "#5B4CA8", "#6E5FB8", "#4C3F92", "#DAD5F2", "#C9C3E6",
            "#14161C", "#3A3F4B", "#5C6373", "#79808F", "#1E8E4A",
            "#1E2029", "#33374A", "#FFFFFF",
        ],
        ["gradient"] =
        [
            "#0E0A1C", "#140F26", "#1B1533", "#181229", "#251C46", "#2E2358",
            "#2A2148", "#382C63", "#443673",
            "#5B3F9E", "#8A5FD6", "#6E4CBF", "#472F86", "#3E2F66",
            "#F6F3FB", "#CFC7DE", "#948CA6", "#7C748E", "#6FD3C5",
            "#F6F3FB", "#FFFFFF", "#0E0A1C",
        ],
        ["sunset"] =
        [
            "#1A0E14", "#221219", "#2B1720", "#26141C", "#3A1F2B", "#4A2836",
            "#3A2029", "#4E2C38", "#5F3746",
            "#A6455C", "#E8766A", "#C25A62", "#7E3446", "#6B3140",
            "#FBF2F3", "#E0C6C7", "#A98D93", "#8E757B", "#F0A05A",
            "#FBF2F3", "#FFFFFF", "#1A0E14",
        ],
        ["crimson"] =
        [
            "#120809", "#1A0C0E", "#221012", "#1D0D0F", "#301619", "#3E1D21",
            "#2C1417", "#3D1D21", "#4C252A",
            "#8E2B33", "#D14A55", "#B03B45", "#6C2027", "#5A1F25",
            "#FBF1F2", "#DEC2C4", "#A78A8D", "#8C7174", "#7DCF8F",
            "#FBF1F2", "#FFFFFF", "#120809",
        ],
        ["amber"] =
        [
            "#120E05", "#1A1409", "#22190B", "#1D1509", "#312414", "#402F1A",
            "#2C2112", "#3D2E19", "#4C3A20",
            "#8A6417", "#D9A227", "#B4841F", "#6A4C11", "#59410F",
            "#FCF7EC", "#E0D2B4", "#A89673", "#8C7F63", "#7DCF8F",
            "#FCF7EC", "#FFFFFF", "#120E05",
        ],
        ["slate"] =
        [
            "#0B0E11", "#111519", "#161B21", "#12171C", "#1F262E", "#28313B",
            "#1C232A", "#28323B", "#33404B",
            "#3E5568", "#6E93AE", "#547491", "#334657", "#2E3E4B",
            "#F1F4F7", "#C4CDD6", "#8794A1", "#6F7B86", "#6FD3A0",
            "#F1F4F7", "#FFFFFF", "#0B0E11",
        ],
    };

    /// <summary>Gradient themes paint the page as a sweep instead of a flat fill.</summary>
    private static IBrush GradientBackground(string id)
    {
        var stops = id == "sunset"
            ? [("#2A0F1B", 0.0), ("#1C1020", 0.5), ("#120C18", 1.0)]
            : new[] { ("#160B2E", 0.0), ("#0D1230", 0.55), ("#08202B", 1.0) };

        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        };
        foreach (var (colour, offset) in stops)
        {
            brush.GradientStops.Add(new GradientStop(Color.Parse(colour), offset));
        }

        return brush;
    }

    public static string Current { get; private set; } = "purple";

    /// <summary>Light themes need dark artwork; the solid-white logo would vanish.</summary>
    public static bool IsLightTheme(string id) => id == "white";

    public static bool IsKnown(string id) => Palettes.ContainsKey(id);

    public static void Apply(string id)
    {
        if (!Palettes.TryGetValue(id, out var palette)) return;
        var resources = Application.Current?.Resources;
        if (resources is null) return;

        for (var i = 0; i < Keys.Length; i++)
        {
            resources[Keys[i]] = new SolidColorBrush(Color.Parse(palette[i]));
        }

        if (id is "gradient" or "sunset") resources["UmBg"] = GradientBackground(id);

        // Card sheen: a GradientStop binds a Color, not a Brush, so these are published separately.
        // Derived from the card colour so every theme keeps the same subtle top-down lift.
        var card = Color.Parse(palette[Array.IndexOf(Keys, "UmCard")]);
        resources["UmCardTop"] = Lighten(card, id == "white" ? 1.0 : 1.10);
        resources["UmCardBottom"] = Lighten(card, id == "white" ? 0.985 : 0.90);

        Current = id;
    }

    /// <summary>Scales a colour's channels, clamped so bright themes don't wrap around to black.</summary>
    private static Color Lighten(Color colour, double factor) => Color.FromRgb(
        (byte)Math.Clamp(colour.R * factor, 0, 255),
        (byte)Math.Clamp(colour.G * factor, 0, 255),
        (byte)Math.Clamp(colour.B * factor, 0, 255));

    /// <summary>Seeds the default palette before the first window is shown.</summary>
    public static void ApplyDefaults() => Apply(Current);

    public static string NameOf(string id) =>
        Themes.FirstOrDefault(t => t.Id == id)?.Name ?? id;
}
