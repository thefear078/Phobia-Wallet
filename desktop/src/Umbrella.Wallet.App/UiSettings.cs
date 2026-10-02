using System;
using System.IO;
using System.Text.Json;
using System.Linq;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.App;

/// <summary>
/// Interface preferences (theme, language). Deliberately separate from the vault: these are not
/// secrets, they must be readable before unlock so the login screen already looks and reads right.
/// </summary>
public sealed class UiSettings
{
    public string Theme { get; set; } = Theming.DefaultTheme;

    /// <summary>
    /// Which rebrand this file has seen. 0 = written by Umbrella, whose default theme was gold and whose
    /// screens carried stickers. The wallet is Phobia now — violet, and clean — so a file from before
    /// moves to the new default theme (if it was still on the old one) and leaves stickers off, once;
    /// anyone who then picks gold or turns stickers back on keeps that.
    /// </summary>
    public int BrandVersion { get; set; }
    public string Language { get; set; } = "en";
    public string Currency { get; set; } = "USD";
    public string SidebarPosition { get; set; } = "Left";
    /// <summary>Phone-style compact layout on the desktop: a narrow centred column and a bottom
    /// tab bar, in a phone-sized window. Off = the normal wide desktop layout.</summary>
    public bool MobileMode { get; set; } = false;
    public bool AnimationsEnabled { get; set; } = true;
    /// <summary>Stickers shown at all (off by default: Phobia's look is clean); they loop only while
    /// the master AnimationsEnabled above is on.</summary>
    public bool StickersEnabled { get; set; } = false;

    /// <summary>Show the accounts a wallet's phrase holds at OTHER apps' paths (found by the scan) in its
    /// assets. Off: the assets are this wallet's own accounts only — the found ones looked like another
    /// wallet's money mixed in.</summary>
    public bool ShowFoundAccounts { get; set; } = false;

    /// <summary>Ask for the wallet password again before a send is signed. On by default: an unlocked
    /// wallet left on a desk could otherwise be emptied by anyone who sat down at it.</summary>
    public bool RequirePasswordForSend { get; set; } = true;

    /// <summary>Show every wallet's balance (as of its last refresh) and their sum in the wallet switcher.</summary>
    public bool ShowAllWalletTotals { get; set; } = false;

    /// <summary>The round "back to top" button that fades in at the bottom right of a page scrolled far
    /// down. On by default.</summary>
    public bool ScrollToTopButton { get; set; } = true;

    /// <summary>Notes the user closed (how a swap works, how buying works). They stay closed until
    /// Settings → Appearance → Interface brings them back.</summary>
    public List<string> DismissedNotices { get; set; } = [];
    /// <summary>Crystals floating slowly up behind the page. On by default; gated by AnimationsEnabled.</summary>
    public bool FloatingCrystals { get; set; } = false;

    /// <summary>Tiny facets of light that twinkle across the page. On by default; gated by AnimationsEnabled.</summary>
    public bool CrystalGlints { get; set; } = true;

    /// <summary>A sweep of light across the balance card now and then. On by default; gated by AnimationsEnabled.</summary>
    public bool CardShine { get; set; } = true;

    /// <summary>Soft drifting "aurora" glow behind the content. Opt-in (off by default) so the default
    /// look stays clean.</summary>
    public bool AuroraEnabled { get; set; } = false;

    /// <summary>Look for a newer release without being asked (shortly after start, then twice a day).
    /// Goes through the same route as everything else, Tor included. Installing still takes a click.</summary>
    public bool AutoUpdateCheck { get; set; } = true;

    /// <summary>Download a newer release as soon as it is found, and verify it, so installing is one click.</summary>
    public bool AutoUpdateDownload { get; set; } = true;

    /// <summary>Idle minutes before the vault auto-locks; 0 disables auto-lock entirely.</summary>
    public int AutoLockMinutes { get; set; } = 5;

    /// <summary>Tor-only kill-switch: when on, the wallet refuses any request that would go to clearnet
    /// (fail-closed), so a dropped or disabled Tor can never silently de-anonymise you.</summary>
    public bool TorOnlyMode { get; set; } = false;

    /// <summary>Whether the user switched the bundled Tor on. Remembered, so the wallet starts it again on
    /// the next launch — before, Tor was off after every restart while the Tor-only kill-switch stayed on,
    /// and every request was refused until somebody found the switch.</summary>
    public bool TorEnabled { get; set; }

    /// <summary>Route all traffic through a user-supplied SOCKS5 proxy instead of the bundled Tor.</summary>
    public bool CustomProxyEnabled { get; set; } = false;
    /// <summary>The user's SOCKS5 proxy, e.g. "socks5://127.0.0.1:9050" (host:port also accepted).</summary>
    public string CustomProxyUri { get; set; } = "";
    /// <summary>IP family for direct connections: "auto", "ipv4" or "ipv6".</summary>
    public string IpMode { get; set; } = "auto";

    /// <summary>The Monero remote node this wallet asks about the chain, as host:port. Empty means
    /// the catalog default. It is a node address only - never a credential - so it is stored in the
    /// plain settings file like every other preference.</summary>
    public string MoneroNode { get; set; } = "";

    /// <summary>Per-chain endpoint overrides as "SYMBOL=url" pairs. These are server addresses, never
    /// credentials - a URL carrying credentials is refused before it can be stored - so they live in
    /// the plain settings file like every other preference.</summary>
    public string ChainEndpoints { get; set; } = "";

    /// <summary>Consecutive wrong vault passwords, and when the last one was entered. Persisted so a
    /// counter cannot be reset by closing the window - which would be easier than waiting. Someone
    /// with file access can edit this, but someone with file access can copy the vault and attack it
    /// offline anyway, where no UI rule applies.</summary>
    public int FailedUnlocks { get; set; }

    public DateTimeOffset LastFailedUnlockUtc { get; set; } = DateTimeOffset.MinValue;
    /// <summary>Seconds after which a copied address is auto-wiped from the clipboard; 0 = never.</summary>
    public int ClipboardAutoClearSeconds { get; set; } = 45;
    /// <summary>Lock the vault immediately whenever the window is minimized, so a shoulder-surfer or
    /// screen-share never catches an unlocked wallet left in the background.</summary>
    public bool LockOnMinimize { get; set; } = false;
    /// <summary>Start every unlock with balances hidden (••••), so amounts aren't shown until you
    /// choose to reveal them — good for use in public.</summary>
    public bool HideBalancesDefault { get; set; } = false;
    /// <summary>Opt-in market-data connector (CoinGecko): adds market cap / FDV / volume to token
    /// pages. Off by default so the privacy-first wallet never contacts a third party you didn't enable.</summary>
    public bool RichMarketData { get; set; } = false;

    /// <summary>Watchlisted tickers, comma-separated. Purely local — a list of coin symbols never
    /// leaves this device and is not tied to any account.</summary>
    public string Watchlist { get; set; } = "";

    /// <summary>A user-chosen label for this wallet, shown in the top bar; blank uses the brand only.</summary>
    public string WalletName { get; set; } = "";

    /// <summary>Absolute paths to the user's own profile images (copied into the data folder when
    /// chosen), so their photos work without the app ever bundling them. Blank = use the defaults.</summary>
    public string AvatarPath { get; set; } = "";
    public string BannerPath { get; set; } = "";
    public string SidebarBackgroundPath { get; set; } = "";

    /// <summary>
    /// Which version of the first-run disclaimer the user has accepted (0 = none yet).
    ///
    /// Versioned rather than a bare flag: if what the wallet has to say about custody, anonymity or
    /// the terms ever changes materially, raising <see cref="FirstRunConsent.CurrentVersion"/> asks
    /// again instead of relying on a tick somebody gave to different words (roadmap L.1/L.3/L.8).
    /// </summary>
    public int AcceptedTermsVersion { get; set; }

    /// <summary>The user's own lock-screen (unlock) background; blank uses the bundled default.</summary>
    public string LockBackgroundPath { get; set; } = "";
    /// <summary>When true, the lock screen shows no background image at all (flat).</summary>
    public bool LockScreenPlain { get; set; } = false;

    private static string Path => System.IO.Path.Combine(AppPaths.DataRoot, "ui-settings.json");

    public static UiSettings Load()
    {
        try
        {
            // Fresh install (incl. after a delete + re-download): pick the OS language if we translate
            // it, so a Ukrainian/Russian/… user isn't dropped into English with no setting to restore.
            if (!File.Exists(Path)) return new UiSettings { Language = DefaultLanguage(), BrandVersion = CurrentBrandVersion };
            var settings = JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(Path)) ?? new UiSettings();
            var changed = MoveToPhobia(settings);
            // A theme that was retired (Matrix, Abyss, Kraken, Solarized, Bitcoin, Monero, WhiteBit) falls
            // back to the default on every load — not only during a migration the file may have passed.
            if (!Theming.Themes.Any(x => x.Id == settings.Theme))
            {
                settings.Theme = Theming.DefaultTheme;
                changed = true;
            }
            if (changed) settings.Save();
            return settings;
        }
        catch
        {
            // Preferences are never worth failing startup over.
            return new UiSettings();
        }
    }

    /// <summary>
    /// The one-time move from Umbrella's gold default to Phobia's blue. Returns true when the file
    /// changed. Pure apart from the object it is given, so the rule is testable.
    /// </summary>
    public static bool MoveToPhobia(UiSettings settings)
    {
        if (settings.BrandVersion >= CurrentBrandVersion) return false;
        // Only a file from before Phobia can be sitting on the old default; after that, gold was a choice.
        if (settings.BrandVersion < 1 && settings.Theme == "umbrella") settings.Theme = Theming.DefaultTheme;
        // Stickers became opt-in with Phobia's clean look (version 2); a choice made after that stands.
        if (settings.BrandVersion < 2) settings.StickersEnabled = false;
        // The floating crystals read as stray pixels on the page, not as decoration (version 3): off
        // once for everybody, still one switch away in Settings → Appearance.
        if (settings.BrandVersion < 3) settings.FloatingCrystals = false;
        settings.BrandVersion = CurrentBrandVersion;
        return true;
    }

    /// <summary>1 = the first Phobia build (blue); 2 = Phobia's violet, clean look; 3 = the beta
    /// (floating crystals off).</summary>
    public const int CurrentBrandVersion = 3;

    /// <summary>The OS UI language if Phobia ships a translation for it, otherwise English.</summary>
    // A fresh install always starts in English; the user can switch language in Settings, and that
    // choice is then persisted. (Previously this followed the OS locale, which surprised users on a
    // non-English Windows by opening in a language they hadn't chosen.)
    private static string DefaultLanguage() => "en";

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataRoot);
            File.WriteAllText(Path, JsonSerializer.Serialize(this));
        }
        catch
        {
            // Read-only install directory: run with defaults rather than crash.
        }
    }

    /// <summary>Applies the stored preferences and returns them.</summary>
    public static UiSettings LoadAndApply()
    {
        var settings = Load();
        if (Theming.IsKnown(settings.Theme)) Theming.Apply(settings.Theme);
        else Theming.ApplyDefaults();
        Loc.Instance.CurrentCode = settings.Language;
        Fx.SetLanguage(settings.Language); // fiat amounts follow the UI language's number format
        return settings;
    }
}
