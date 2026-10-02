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

    /// <summary>
    /// Every theme is meant to be its OWN place, not the same app with the accent swapped. Each one
    /// therefore sets its own base hue (warm charcoal, abyssal indigo, phosphor black…) and its own
    /// "positive" green rather than sharing one generic mint — brand themes use the real up-colour
    /// their exchange uses, so a Binance user sees Binance's green.
    /// </summary>
    public static IReadOnlyList<ThemeOption> Themes { get; } =
    [
        new("phobia", "Phobia · midnight violet"),
        new("umbrella", "Honey gold · light on black"),
        new("navy", "Navy · the classic blue"),
        new("ice", "Ice · Phobia blue"),
        new("purple", "The fear · monochrome noir"),
        new("signal", "Ember · crimson editorial"),
        new("black", "Void · electric OLED"),
        new("sunset", "Sunset · dusk gradient"),
        new("nord", "Nord · arctic frost"),
        new("dracula", "Dracula · midnight dev"),
        new("ethereum", "Ethereum · periwinkle"),
        new("solana", "Solana · neon gradient"),
        new("uniswap", "Uniswap · hot pink"),
        new("binance", "Binance · gold"),
        new("telegram", "Telegram · sky"),
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
        // Phobia — the signature look, and the default. Bold and quiet: a near-black page with a breath of
        // violet in it, dark charcoal cards on hairline edges, one violet accent taken from the crystal's
        // deep facets (white labels on it, 6.3:1), the crystal's own blue kept for the logos. Everything
        // else is left out on purpose.
        // bg        bgAlt      card       input      cardAlt    hover      bd         bd2        bd3        accent     accentBr   accentHv   accentSel  accentDim  text       textSoft   textDim    textMut    pos        inverse    inverseHv  inverseTx
        ["phobia"] =
        [
            "#0A0A10", "#0D0D14", "#14141C", "#101018", "#1A1A24", "#20202C",
            "#22222E", "#2C2C3A", "#3A3A4C",
            "#5B3FE8", "#5B3FE8", "#7A63F5", "#231A55", "#1C1640",
            "#F4F4F8", "#C9C9D6", "#8C8CA0", "#6C6C82", "#3DDC97",
            "#F4F4F8", "#FFFFFF", "#0A0A10",
        ],
        // Honey gold — Umbrella's signature look and its default, kept under its old id: gold light on black. Near-neutral blacks with
        // only a breath of warmth, so the gold reads as light on the surface rather than a tint over it;
        // cards a step up from the page with hairline borders; clean white type; gains in a clear green
        // that sits beside gold without competing. Accent buttons take dark labels.
        // bg        bgAlt      card       input      cardAlt    hover      bd         bd2        bd3        accent     accentBr   accentHv   accentSel  accentDim  text       textSoft   textDim    textMut    pos        inverse    inverseHv  inverseTx
        ["umbrella"] =
        [
            "#070707", "#0A0A0A", "#101010", "#0C0C0C", "#161615", "#1D1C19",
            "#1F1E1B", "#2A2825", "#3B3830",
            "#E9B22E", "#F7C531", "#FFD75A", "#3A2C0A", "#2C220A",
            "#F7F7F5", "#DAD7D0", "#A7A299", "#85807A", "#37D67A",
            "#F7F7F5", "#FFFFFF", "#070707",
        ],
        // Navy — the original signature: deep navy/graphite base (never pure black), matte glass cards,
        // cool-white text and the cyan→blue→violet accent. Kept for everyone who chose it.
        // Ice — the Android design: deep navy pages, blue-tinted cards on hairline edges, one bright
        // crystal blue. White labels sit on its accent at 4.8:1.
        ["ice"] =
        [
            "#071021", "#0A1528", "#0E1B31", "#0B172B", "#11223C", "#16294A",
            "#1A2B47", "#213659", "#2B4672",
            "#1E6FE0", "#3EA2FF", "#2A7BEE", "#11305A", "#173A6A",
            "#F2F7FF", "#C7D3E8", "#8FA0BD", "#6E819F", "#3CD989",
            "#F2F7FF", "#FFFFFF", "#071021",
        ],
        ["navy"] =
        [
            "#080D16", "#0B1220", "#111927", "#0F1826", "#151E2D", "#1B2740",
            "#1E2A3D", "#26344A", "#33455F",
            "#3478FF", "#2563EB", "#3B82F6", "#16233C", "#1E3A6B",
            "#F7F9FC", "#C7D0DE", "#8994A7", "#626D80", "#45E6A5",
            "#F7F9FC", "#FFFFFF", "#080D16",
        ],
        // bg       bgAlt    card     input    cardAlt  hover    bd       bd2      bd3      accent   accentBr accentHv accentSel accentDim text    textSoft textDim  textMut  pos
        // Primary "the fear" look: monochrome noir — near-black with cool white accents (the
        // FROSTFREED / reference mood). The accent is near-white, so accent-filled buttons read
        // white-on-black like the references. The red editorial look lives on as the Ember theme.
        ["purple"] =
        [
            "#050506", "#0C0D0F", "#131417", "#101114", "#1C1E22", "#24272C",
            "#1E2024", "#2A2D33", "#373B42",
            "#AEB6C2", "#EDF1F6", "#FFFFFF", "#2A2E36", "#22262C",
            "#F4F6F9", "#C4CBD4", "#8A929C", "#6C737C", "#7FD69A",
            "#F4F6F9", "#FFFFFF", "#050506",
        ],
        // Ember — crimson editorial on a WARM charcoal (the base carries a red undertone, so the theme
        // reads as one warm room rather than a red accent dropped on neutral grey).
        // bg        bgAlt      card       input      cardAlt    hover      bd         bd2        bd3        accent     accentBr   accentHv   accentSel  accentDim  text       textSoft   textDim    textMut    pos        inverse    inverseHv  inverseTx
        ["signal"] =
        [
            "#0A0708", "#120C0E", "#181012", "#140D0F", "#221618", "#2E1D20",
            "#201417", "#2E1E21", "#40292D",
            "#C42230", "#DE1F33", "#EE3244", "#2E1013", "#260C0F",
            "#F7F3F4", "#D6C9CB", "#9A8A8D", "#746668", "#3FD98A",
            "#F7F3F4", "#FFFFFF", "#0A0708",
        ],
        // Void — a true OLED black (#000000 really is off pixels) with the electric-cyan glass accent
        // the theme always promised. It used to be flat grey on grey, which made it the least
        // distinctive theme in the list despite the boldest name.
        ["black"] =
        [
            "#000000", "#050607", "#0B0D0E", "#08090A", "#121517", "#1A1F22",
            "#16191B", "#232829", "#333A3C",
            "#0FA8BF", "#22D3EE", "#4AE3F7", "#072A31", "#052126",
            "#FFFFFF", "#C9D2D4", "#93A0A3", "#6E7A7D", "#34D399",
            "#FFFFFF", "#E8F6F8", "#000000",
        ],
        ["sunset"] =
        [
            "#1A0E14", "#221219", "#2B1720", "#26141C", "#3A1F2B", "#4A2836",
            "#3A2029", "#4E2C38", "#5F3746",
            "#A6455C", "#E8766A", "#C25A62", "#7E3446", "#6B3140",
            "#FBF2F3", "#E0C6C7", "#A98D93", "#8E757B", "#F0A05A",
            "#FBF2F3", "#FFFFFF", "#1A0E14",
        ],
        // Teal cyber on gunmetal — the tech/HUD mood.
        // Uniswap: the exact hot-pink (#FF007A) on Uniswap's neutral near-black (app.uniswap.org dark).
        ["uniswap"] =
        [
            "#0D0E0E", "#131415", "#191A1C", "#141517", "#202224", "#2A2D30",
            "#1E2022", "#2C2F33", "#3A3E43",
            "#D6006B", "#FF007A", "#FF4D9E", "#3A0B22", "#2E091B",
            "#F5F6F7", "#CBD0D4", "#8D9499", "#727980", "#21C77A",
            "#F5F6F7", "#FFFFFF", "#0D0E0E",
        ],
        // Binance — signature black + gold (#F0B90B), with Binance's OWN up-green (#0ECB81) rather
        // than a generic mint, so gains look the way they do on the exchange itself.
        ["binance"] =
        [
            "#0B0E11", "#12161B", "#181D24", "#141920", "#20262F", "#2A323C",
            "#1C222A", "#2A323C", "#38424F",
            "#B88A08", "#F0B90B", "#F5C838", "#3A2F0A", "#2E2608",
            "#EAECEF", "#C7CDD4", "#848E9C", "#6A7482", "#0ECB81",
            "#EAECEF", "#FFFFFF", "#0B0E11",
        ],
        // Telegram — its own blue (#2AABEE) on the Telegram-dark surface, and Telegram's own green.
        ["telegram"] =
        [
            "#0E1621", "#17212B", "#1C2733", "#182430", "#22303C", "#2B3B47",
            "#1E2A36", "#2A3947", "#38495A",
            "#1E88C8", "#2AABEE", "#3FBEFF", "#123449", "#0F2A3A",
            "#EAF3FA", "#B9CFDD", "#7E96A6", "#647B8A", "#4DCD5E",
            "#EAF3FA", "#FFFFFF", "#0E1621",
        ],
        // Solana — the brand IS a gradient, so this theme paints the page as one (see
        // GradientBackground) and uses Solana's own #14F195 green for gains.
        ["solana"] =
        [
            "#07060A", "#0C0A12", "#121019", "#0E0C15", "#1C1826", "#262034",
            "#1A1626", "#282236", "#3A3250",
            "#7C3AED", "#9945FF", "#B06BFF", "#241546", "#1B1035",
            "#F4F1FA", "#D2CCE4", "#9B93B0", "#7A7291", "#14F195",
            "#F4F1FA", "#FFFFFF", "#07060A",
        ],
        // Ethereum — the #627EEA periwinkle on a cool blue-grey near-black.
        ["ethereum"] =
        [
            "#06070C", "#0B0D15", "#10131D", "#0D1018", "#1A1E2B", "#232839",
            "#181C28", "#262B3B", "#363D52",
            "#4C63C7", "#627EEA", "#8098FF", "#182046", "#121936",
            "#F2F4FB", "#CBD1E4", "#939BB4", "#737B94", "#5BD6A0",
            "#F2F4FB", "#FFFFFF", "#06070C",
        ],
        // Nord — the arctic palette: cool slate greys, frost-blue accent.
        ["nord"] =
        [
            "#242933", "#2E3440", "#3B4252", "#353C4A", "#434C5E", "#4C566A",
            "#434C5E", "#4C566A", "#616E88",
            "#5E81AC", "#88C0D0", "#8FBCBB", "#3B4252", "#2E3440",
            "#ECEFF4", "#D8DEE9", "#A9B3C4", "#8892A4", "#A3BE8C",
            "#ECEFF4", "#FFFFFF", "#242933",
        ],
        // Dracula — the classic dev theme: #BD93F9 purple, #50FA7B green, on #282A36.
        ["dracula"] =
        [
            "#21222C", "#282A36", "#343746", "#2C2E3A", "#44475A", "#4E5267",
            "#343746", "#44475A", "#565A70",
            "#9B72E0", "#BD93F9", "#D0AEFF", "#3A2F55", "#2E2545",
            "#F8F8F2", "#DCDCE4", "#A8A8B8", "#8A8A9C", "#50FA7B",
            "#F8F8F2", "#FFFFFF", "#21222C",
        ],
    };

    /// <summary>The themes whose page background is a sweep rather than a flat fill. Solana is here
    /// because its brand identity IS a gradient; Sunset because dusk has no single colour.</summary>
    private static readonly HashSet<string> GradientThemes = new(StringComparer.Ordinal) { "sunset", "solana", "phobia" };

    /// <summary>
    /// The page of every other dark theme, in the Phobia manner: its own background, lifted by a soft glow
    /// of its accent a little right of centre — so each theme reads as the same designed place in its
    /// own colours, where it used to be a flat fill behind the cards. Light themes stay flat.
    /// </summary>
    private static IBrush AccentGlow(string[] palette)
    {
        var bg = Color.Parse(palette[Array.IndexOf(Keys, "UmBg")]);
        var accent = Color.Parse(palette[Array.IndexOf(Keys, "UmAccent")]);
        Color Mix(double t) => Color.FromRgb(
            (byte)Math.Round(bg.R + ((accent.R - bg.R) * t)),
            (byte)Math.Round(bg.G + ((accent.G - bg.G) * t)),
            (byte)Math.Round(bg.B + ((accent.B - bg.B) * t)));

        var glow = new RadialGradientBrush
        {
            Center = new RelativePoint(0.62, 0.42, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.62, 0.42, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.75, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.85, RelativeUnit.Relative),
        };
        glow.GradientStops.Add(new GradientStop(Mix(0.16), 0.0));
        glow.GradientStops.Add(new GradientStop(Mix(0.05), 0.45));
        glow.GradientStops.Add(new GradientStop(bg, 1.0));
        return glow;
    }

    /// <summary>Gradient themes paint the page as a sweep instead of a flat fill.</summary>
    private static IBrush GradientBackground(string id)
    {
        if (id == "phobia")
        {
            // Near-black, with a breath of violet light from the top right — and nothing else.
            var glow = new RadialGradientBrush
            {
                Center = new RelativePoint(0.85, 0.0, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.85, 0.0, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.9, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.9, RelativeUnit.Relative),
            };
            glow.GradientStops.Add(new GradientStop(Color.Parse("#19132F"), 0.0));
            glow.GradientStops.Add(new GradientStop(Color.Parse("#0D0C15"), 0.55));
            glow.GradientStops.Add(new GradientStop(Color.Parse("#0A0A10"), 1.0));
            return glow;
        }

        var stops = id switch
        {
            "sunset" => [("#2A0F1B", 0.0), ("#1C1020", 0.5), ("#120C18", 1.0)],
            // Solana's purple → teal sweep, darkened to stay a background rather than a poster.
            _ => new[] { ("#1A0B33", 0.0), ("#120C28", 0.5), ("#07211D", 1.0) },
        };

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

    public static string Current { get; private set; } = DefaultTheme;

    /// <summary>The theme a fresh install opens in — Phobia's midnight violet.</summary>
    public const string DefaultTheme = "phobia";

    /// <summary>Light themes need dark artwork; the solid-white logo would vanish. Decided from the
    /// palette's own background rather than a hardcoded id — the id this used to compare against
    /// ("white") named a theme that no longer exists, so it silently answered false for everything.</summary>
    public static bool IsLightTheme(string id) =>
        Palettes.TryGetValue(id, out var palette)
        && Luminance(Color.Parse(palette[Array.IndexOf(Keys, "UmBg")])) > 0.5;

    public static bool IsKnown(string id) => Palettes.ContainsKey(id);

    /// <summary>
    /// A theme's raw colours keyed by resource name, or null for an unknown id. Exposed so the palettes
    /// can be checked offline — contrast, distinctness, completeness — without standing up an Avalonia
    /// Application just to read <c>Application.Current.Resources</c>.
    /// </summary>
    public static IReadOnlyDictionary<string, string>? PaletteOf(string id) =>
        Palettes.TryGetValue(id, out var palette)
            ? Keys.Select((k, i) => (k, palette[i])).ToDictionary(x => x.k, x => x.Item2, StringComparer.Ordinal)
            : null;

    /// <summary>Near-black, the dark option for a label sitting on an accent fill.</summary>
    private static readonly Color AccentTextDark = Color.Parse("#0A0A0B");

    /// <summary>
    /// The label colour an accent-filled button uses: whichever of near-black or white actually reads
    /// better ON that accent, measured with the WCAG contrast formula.
    ///
    /// This used to be a brightness threshold ("brighter than 0.62 → dark text"). A mid-bright accent
    /// such as Sunset's coral fell just under the line and got white text at 2.9:1, when dark text on
    /// the same colour gives 6.8:1. Comparing the two candidates directly removes the guess, and keeps
    /// working for any accent added later. Shared with the palette tests so the rule is verified where
    /// it is defined.
    /// </summary>
    public static Color AccentTextFor(Color accent) =>
        WcagContrast(AccentTextDark, accent) >= WcagContrast(Colors.White, accent)
            ? AccentTextDark
            : Colors.White;

    /// <summary>WCAG 2.x relative luminance (gamma-corrected), for contrast ratios.</summary>
    private static double RelativeLuminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));
    }

    /// <summary>WCAG contrast ratio between two colours, 1:1 (identical) to 21:1 (black on white).</summary>
    private static double WcagContrast(Color a, Color b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        var (hi, lo) = la > lb ? (la, lb) : (lb, la);
        return (hi + 0.05) / (lo + 0.05);
    }

    /// <summary>True when this theme paints its page as a gradient rather than a flat colour.</summary>
    public static bool IsGradientTheme(string id) => GradientThemes.Contains(id);

    public static void Apply(string id)
    {
        if (!Palettes.TryGetValue(id, out var palette)) return;
        var app = Application.Current?.Resources;
        if (app is null) return;

        // Built off to the side and swapped in whole. Writing each of the ~50 keys into the application's
        // resources made every control in the window re-read its resources ~50 times: switching theme
        // froze the window for most of a second.
        var resources = new Avalonia.Controls.ResourceDictionary();

        for (var i = 0; i < Keys.Length; i++)
        {
            resources[Keys[i]] = new SolidColorBrush(Color.Parse(palette[i]));
        }

        if (GradientThemes.Contains(id)) resources["UmBg"] = GradientBackground(id);
        else if (!IsLightTheme(id)) resources["UmBg"] = AccentGlow(palette);

        // Card sheen: a GradientStop binds a Color, not a Brush, so these are published separately.
        // Derived from the card colour so every theme keeps the same subtle top-down lift. A light
        // theme has to lift DOWNWARD (darken) or the sheen would blow out to white.
        var light = IsLightTheme(id);
        var card = Color.Parse(palette[Array.IndexOf(Keys, "UmCard")]);
        resources["UmCardTop"] = Lighten(card, light ? 1.0 : 1.10);
        resources["UmCardBottom"] = Lighten(card, light ? 0.985 : 0.90);

        // Readable text colour ON the accent, chosen by the accent's brightness — so accent-filled
        // buttons stay legible whether the theme accent is dark (red/violet) or light (mint/cyan).
        var accent = Color.Parse(palette[Array.IndexOf(Keys, "UmAccentBright")]);
        resources["UmAccentText"] = new SolidColorBrush(AccentTextFor(accent));
        // A soft translucent wash of the accent, for hover fills and glows that follow the theme.
        resources["UmAccentWash"] = new SolidColorBrush(accent) { Opacity = 0.16 };

        // Glow colours for the splash and aurora effects, which used to be a fixed brand blue and
        // showed as blue smudges on every other theme. GradientStops take Colors, not brushes.
        var bg = Color.Parse(palette[Array.IndexOf(Keys, "UmBg")]);
        resources["UmGlow"] = accent;
        resources["UmGlowSoft"] = Color.FromArgb(0x55, accent.R, accent.G, accent.B);
        resources["UmGlowFaint"] = Color.FromArgb(0x1A, accent.R, accent.G, accent.B);
        resources["UmGlowClear"] = Color.FromArgb(0x00, accent.R, accent.G, accent.B);
        resources["UmBgClear"] = Color.FromArgb(0x00, bg.R, bg.G, bg.B);

        // The crystal logo and the crystals behind the window (Controls/CrystalArt) keep their own blues
        // on the Phobia theme and take this theme's hue everywhere else; transparent means "as drawn".
        resources["UmCrystalTint"] = id == DefaultTheme ? Colors.Transparent : accent;

        // A soft fill in this theme's colours for marks drawn as a silhouette: light where it catches
        // the light, the accent through the middle, deeper at the far edge.
        var markFill = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        };
        markFill.GradientStops.Add(new GradientStop(Color.Parse(palette[Array.IndexOf(Keys, "UmAccentHover")]), 0.0));
        markFill.GradientStops.Add(new GradientStop(accent, 0.45));
        markFill.GradientStops.Add(new GradientStop(Color.Parse(palette[Array.IndexOf(Keys, "UmAccent")]), 1.0));
        resources["UmMarkFill"] = markFill;

        // Under every chart line: a wash of the accent fading to nothing, so the line reads as light.
        var chartArea = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        };
        chartArea.GradientStops.Add(new GradientStop(Color.FromArgb(0x55, accent.R, accent.G, accent.B), 0.0));
        chartArea.GradientStops.Add(new GradientStop(Color.FromArgb(0x16, accent.R, accent.G, accent.B), 0.55));
        chartArea.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, accent.R, accent.G, accent.B), 1.0));
        resources["UmChartArea"] = chartArea;

        // The mark's tile: a hairline of the accent and a glow of it, like the app icon's own edge.
        resources["UmTileEdge"] = new SolidColorBrush(accent) { Opacity = 0.6 };
        resources["UmTileGlow"] = new BoxShadows(new BoxShadow
        {
            Blur = 22,
            Color = Color.FromArgb(0x40, accent.R, accent.G, accent.B),
        });

        // Hero balance-card gradient, DERIVED from the theme so the card never clashes with the palette.
        // A fixed violet gradient used to sit under every theme, which "spoiled" the reds/greens/etc.
        // Card → a dark tint of the theme accent → card-alt, on a diagonal — a premium, on-theme surface.
        var heroCard = Color.Parse(palette[Array.IndexOf(Keys, "UmCard")]);
        var heroTint = Color.Parse(palette[Array.IndexOf(Keys, "UmAccentDim")]);
        var heroEnd = Color.Parse(palette[Array.IndexOf(Keys, "UmCardAlt")]);
        var hero = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        };
        hero.GradientStops.Add(new GradientStop(heroCard, 0.0));
        hero.GradientStops.Add(new GradientStop(heroTint, 0.55));
        hero.GradientStops.Add(new GradientStop(heroEnd, 1.0));
        resources["UmHeroGradient"] = hero;

        PublishSignature(resources, palette, hero, accent);
        if (id == DefaultTheme) PublishPhobiaSignature(resources);

        // The application's own defaults for these keys (App.axaml) would win over a merged dictionary,
        // so they step aside the first time; from then on the theme lives only in its dictionary.
        foreach (var key in resources.Keys.ToList())
        {
            if (app.ContainsKey(key)) app.Remove(key);
        }

        var at = _applied is null ? -1 : app.MergedDictionaries.IndexOf(_applied);
        if (at >= 0) app.MergedDictionaries[at] = resources;
        else app.MergedDictionaries.Add(resources);
        _applied = resources;

        Current = id;
    }

    /// <summary>The theme dictionary in use, replaced whole by the next <see cref="Apply"/>.</summary>
    private static Avalonia.Controls.ResourceDictionary? _applied;

    /// <summary>
    /// The balance card, the action tiles and the active navigation item: one treatment on every theme,
    /// in that theme's colours — dark surfaces, with the accent used as light. Every theme publishes
    /// every one of these, so nothing from the previous theme can linger after a switch.
    /// </summary>
    private static void PublishSignature(
        Avalonia.Controls.IResourceDictionary resources, string[] palette, IBrush hero, Color accent)
    {
        IBrush Solid(string key) => new SolidColorBrush(Color.Parse(palette[Array.IndexOf(Keys, key)]));

        resources["UmHeroSurface"] = hero;
        resources["UmHeroWash"] = new SolidColorBrush(accent) { Opacity = 0.16 };
        resources["UmHeroEdge"] = Solid("UmBorder");
        resources["UmHeroText"] = Solid("UmText");
        resources["UmHeroTextSoft"] = Solid("UmTextDim");
        resources["UmHeroAccent"] = Solid("UmAccentBright");
        resources["UmHeroUnderlay"] = Brushes.Transparent;
        resources["UmHeroShade"] = Color.Parse("#B3060B14");
        resources["UmHeroShadeClear"] = Color.Parse("#00060B14");

        // Action tiles: dark, with the accent only in the icon — and a hairline of it on hover.
        resources["UmDiscFill"] = Solid("UmCard");
        resources["UmDiscEdge"] = Solid("UmBorder2");
        resources["UmDiscIcon"] = Solid("UmAccentBright");
        resources["UmDiscFillHover"] = Solid("UmCardAlt");
        resources["UmDiscEdgeHover"] = new SolidColorBrush(accent) { Opacity = 0.45 };
        resources["UmDiscIconHover"] = Solid("UmAccentHover");

        // The page you are on: a warm wash of the accent fading to the right, the icon lit in it.
        var navFill = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
        };
        navFill.GradientStops.Add(new GradientStop(Color.FromArgb(0x38, accent.R, accent.G, accent.B), 0.0));
        navFill.GradientStops.Add(new GradientStop(Color.FromArgb(0x0C, accent.R, accent.G, accent.B), 1.0));
        resources["UmNavActiveFill"] = navFill;
        resources["UmNavActiveIcon"] = Solid("UmAccentBright");
        resources["UmNavActiveText"] = Solid("UmText");

        // Buttons and card edges in this theme's plain form; Phobia replaces them with its own.
        resources["UmPrimaryFill"] = Solid("UmAccentBright");
        resources["UmPrimaryFillHover"] = Solid("UmAccentHover");
        resources["UmPrimaryGlow"] = new BoxShadows(new BoxShadow { Color = Colors.Transparent });
        resources["UmCardEdge"] = Solid("UmBorder");
        resources["UmAccent2"] = Solid("UmAccentHover");
    }

    /// <summary>
    /// Phobia's own finish, on top of the treatment every theme gets — restraint more than decoration:
    /// <list type="bullet">
    /// <item>Call-to-action buttons are one solid violet with a soft glow of it underneath.</item>
    /// <item>Cards are charcoal on a hairline edge that is a shade lighter at the top.</item>
    /// <item>The balance card is the same charcoal with violet light in its top-right corner.</item>
    /// <item>The page you are on is marked by a violet wash.</item>
    /// </list>
    /// Every other theme publishes the plain versions of the same keys (PublishSignature), so nothing of
    /// Phobia's lingers after a switch.
    /// </summary>
    private static void PublishPhobiaSignature(Avalonia.Controls.IResourceDictionary resources)
    {
        static LinearGradientBrush Sweep(double x1, double y1, double x2, double y2, params (string Colour, double At)[] stops)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(x1, y1, RelativeUnit.Relative),
                EndPoint = new RelativePoint(x2, y2, RelativeUnit.Relative),
            };
            foreach (var (colour, at) in stops) brush.GradientStops.Add(new GradientStop(Color.Parse(colour), at));
            return brush;
        }

        resources["UmPrimaryFill"] = new SolidColorBrush(Color.Parse("#5B3FE8"));
        resources["UmPrimaryFillHover"] = new SolidColorBrush(Color.Parse("#6A50F0"));
        resources["UmPrimaryGlow"] = new BoxShadows(new BoxShadow
        {
            OffsetY = 8, Blur = 24, Spread = -10, Color = Color.Parse("#905B3FE8"),
        });
        resources["UmCardEdge"] = Sweep(0, 0, 0, 1, ("#30303F", 0), ("#1E1E29", 1));

        var hero = new RadialGradientBrush
        {
            Center = new RelativePoint(1.0, 0.0, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(1.0, 0.0, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(1.1, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(1.3, RelativeUnit.Relative),
        };
        hero.GradientStops.Add(new GradientStop(Color.Parse("#2E2270"), 0.0));
        hero.GradientStops.Add(new GradientStop(Color.Parse("#17162A"), 0.55));
        hero.GradientStops.Add(new GradientStop(Color.Parse("#14141C"), 1.0));
        resources["UmHeroSurface"] = hero;
        resources["UmHeroWash"] = Brushes.Transparent;
        resources["UmHeroEdge"] = new SolidColorBrush(Color.Parse("#2A2A3A"));
        resources["UmNavActiveFill"] = Sweep(0, 0.5, 1, 0.5, ("#385B3FE8", 0), ("#085B3FE8", 1));
        resources["UmAccent2"] = new SolidColorBrush(Color.Parse("#4E7BFF"));
    }

    /// <summary>Perceptual-ish brightness in 0..1, to decide dark-vs-light text on a colour.</summary>
    private static double Luminance(Color c) => ((0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B)) / 255.0;

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
