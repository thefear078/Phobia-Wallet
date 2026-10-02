using System.Reflection;
using System.Text.RegularExpressions;
using Umbrella.Wallet.App;
using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The Android app: the same view models and pages as the desktop, shown through Views/MobileShell.
/// What is phone-only is checked here - the wording that names the device, the back gesture, the two
/// sheets, and the APK's version code following VERSION.
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class PhoneLayoutTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"umbrella-phone-{Guid.NewGuid():N}");

    public PhoneLayoutTests() => MainViewModel.FetchCurrencyOnStart = false;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { /* best effort */ }
    }

    private MainViewModel NewViewModel()
    {
        TestDataIsolation.RestoreBaselineSettings();
        return new(new EncryptedFileSeedVault(Path.Combine(_directory, "vault.json")));
    }

    private static Dictionary<string, Dictionary<string, string>> Tables()
    {
        var field = typeof(Loc).GetField("Strings", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        return (Dictionary<string, Dictionary<string, string>>)field!.GetValue(null)!;
    }

    // The ways each language says "this computer". A new string that says it another way would read
    // "on this PC" on a phone; add the phrasing to Loc.DeviceWording and here.
    private static readonly Dictionary<string, string> PcPhrases = new()
    {
        ["en"] = @"\bthis (PC|computer)\b|\bThis PC\b",
        ["uk"] = @"цьому ПК|цьому комп'ютері|вашому ПК|цей ПК",
        ["ru"] = @"этом ПК|этого ПК|этом компьютере|вашем компьютере|этим компьютером|этот компьютер",
        ["de"] = @"diesem (PC|Rechner|Computer)|dieser PC",
        ["es"] = @"este (PC|equipo|ordenador)",
        ["zh"] = @"这台电脑|此电脑|本电脑",
    };

    [Fact]
    public void On_a_phone_no_string_says_this_PC()
    {
        var left = new List<string>();
        foreach (var (code, table) in Tables())
        {
            Assert.True(PcPhrases.ContainsKey(code), $"no PC phrasing listed for {code}");
            var pattern = new Regex(PcPhrases[code]);
            foreach (var (key, text) in table)
            {
                var phone = Loc.ForDevice(text, code);
                if (pattern.IsMatch(phone)) left.Add($"{code}:{key} → {pattern.Match(phone).Value}");
            }
        }

        Assert.True(left.Count == 0, "still says PC on a phone: " + string.Join("; ", left.Take(10)));
    }

    [Fact]
    public void Device_wording_changes_the_device_and_nothing_else()
    {
        Assert.Equal("Keys encrypted on this device", Loc.ForDevice("Keys encrypted on this PC", "en"));
        Assert.Equal("Ключі зашифровано на цьому пристрої", Loc.ForDevice("Ключі зашифровано на цьому ПК", "uk"));
        Assert.Equal("Erase from this device", Loc.ForDevice("Erase from this PC", "en"));
        // A sentence about some other computer is left as it is.
        Assert.Equal("Plug the Ledger into a PC", Loc.ForDevice("Plug the Ledger into a PC", "en"));
        Assert.Equal("anything", Loc.ForDevice("anything", "xx"));
    }

    [Fact]
    public void Sheets_close_each_other_and_back_closes_a_sheet_first()
    {
        var vm = NewViewModel();

        vm.ToggleQuickSheetCommand.Execute(null);
        Assert.True(vm.IsQuickSheetOpen);
        vm.ToggleWalletSheetCommand.Execute(null);
        Assert.True(vm.IsWalletSheetOpen);
        Assert.False(vm.IsQuickSheetOpen);

        Assert.True(vm.GoBack());
        Assert.False(vm.IsWalletSheetOpen);
        Assert.False(vm.IsQuickSheetOpen);

        // Locked, nothing open: back leaves the app.
        Assert.False(vm.GoBack());
    }

    [Fact]
    public void A_quick_action_closes_the_sheet_and_opens_its_section()
    {
        var vm = NewViewModel();
        vm.ToggleQuickSheetCommand.Execute(null);

        vm.QuickActionCommand.Execute("Receive");

        Assert.False(vm.IsQuickSheetOpen);
        Assert.Equal("Receive", vm.ActiveSection);
    }

    [Fact]
    public void The_chart_caption_follows_the_range()
    {
        var vm = NewViewModel();
        foreach (var range in new[] { "1D", "1W", "1M", "1Y" })
        {
            vm.PortfolioRange = range;
            Assert.Equal(Loc.Instance["mobile.perf." + range], vm.PortfolioCaption);
            Assert.NotEqual("mobile.perf." + range, vm.PortfolioCaption);
        }
    }

    /// <summary>
    /// Android installs an update only when its versionCode is higher. It is derived from VERSION -
    /// major·1000000 + minor·10000 + patch·100 + beta number (99 for the full release, so the release
    /// sorts after its betas) - and checked here so a version bump cannot leave it behind.
    /// </summary>
    [Fact]
    public void The_apk_version_follows_VERSION()
    {
        var root = RepoRoot();
        var version = File.ReadAllText(Path.Combine(root, "VERSION")).Trim();
        var match = Regex.Match(version, @"^(\d+)\.(\d+)\.(\d+)(?:-beta\.(\d+))?$");
        Assert.True(match.Success, version);
        int Part(int i) => int.Parse(match.Groups[i].Value);
        var beta = match.Groups[4].Success ? Part(4) : 99;
        var code = Part(1) * 1_000_000 + Part(2) * 10_000 + Part(3) * 100 + beta;

        var csproj = File.ReadAllText(Path.Combine(root, "desktop", "src", "Umbrella.Wallet.Android", "Umbrella.Wallet.Android.csproj"));
        Assert.Contains($"<ApplicationVersion>{code}</ApplicationVersion>", csproj);
        Assert.Contains($"<ApplicationDisplayVersion>{match.Groups[1].Value}.{match.Groups[2].Value}.{match.Groups[3].Value}</ApplicationDisplayVersion>", csproj);
    }

    /// <summary>
    /// The desktop renders seed and key screens black to screen capture; the phone must do the same, or a
    /// screenshot app - or the recent-apps thumbnail - keeps a copy of the seed.
    /// </summary>
    [Fact]
    public void The_phone_hides_a_secret_screen_from_screenshots()
    {
        var root = RepoRoot();
        var shell = File.ReadAllText(Path.Combine(root, "desktop", "src", "Umbrella.Wallet.App", "Views", "MobileShell.axaml.cs"));
        foreach (var secret in new[] { "IsBackupStage", "IsSettingsPhraseVisible", "IsMoneroKeysVisible" })
            Assert.Contains($"vm.{secret}", shell);

        var activity = File.ReadAllText(Path.Combine(root, "desktop", "src", "Umbrella.Wallet.Android", "MainActivity.cs"));
        Assert.Contains("MobileShell.SecretOnScreenChanged += ProtectScreen", activity);
        Assert.Contains("AddFlags(WindowManagerFlags.Secure)", activity);

        // And nothing of the wallet goes into Android's cloud backup.
        var manifest = File.ReadAllText(Path.Combine(root, "desktop", "src", "Umbrella.Wallet.Android", "Properties", "AndroidManifest.xml"));
        Assert.Contains("android:allowBackup=\"false\"", manifest);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "desktop", "src"))) return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("repo root not found from " + AppContext.BaseDirectory);
    }
}
