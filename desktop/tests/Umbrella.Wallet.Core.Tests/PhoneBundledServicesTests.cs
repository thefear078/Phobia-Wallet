using Umbrella.Wallet.App;
using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.Infrastructure;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The APK now carries Tor and monero-wallet-rpc in its native-library folder (the one place Android runs
/// a program from), and the Android host points the wallet at them. On a phone with them the start-up
/// rules are the desktop's: "Tor on" and the kill-switch survive a restart, and the "no Tor on the
/// phone" note is gone.
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class PhoneBundledServicesTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"umbrella-phone-tor-{Guid.NewGuid():N}");

    public PhoneBundledServicesTests()
    {
        MainViewModel.FetchCurrencyOnStart = false;
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        EmbeddedTorService.ExecutableOverride = null;
        MoneroRpcService.ExecutableOverride = null;
        MainViewModel.HasBundledServices = !OperatingSystem.IsAndroid();
        MainViewModel.FetchCurrencyOnStart = true;
        TestDataIsolation.RestoreBaselineSettings();
        TestDataIsolation.GoOffline();
        try { Directory.Delete(_directory, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void A_phone_with_its_own_tor_keeps_tor_only_across_a_restart_and_drops_the_orbot_note()
    {
        MainViewModel.HasBundledServices = true;
        Settings("""{"TorOnlyMode":true,"TorEnabled":true}""");

        var vm = new MainViewModel(new EncryptedFileSeedVault(Path.Combine(_directory, "vault.json")));

        Assert.True(vm.TorOnly);
        Assert.True(PublicHttp.RequireProxy);    // armed from the first request: nothing goes direct
        Assert.False(vm.LacksBundledServices);   // the "Tor is desktop-only" note is not shown
    }

    [Fact]
    public void Without_them_the_phone_still_says_so()
    {
        MainViewModel.HasBundledServices = false;
        Settings("""{"TorOnlyMode":false}""");

        var vm = new MainViewModel(new EncryptedFileSeedVault(Path.Combine(_directory, "vault.json")));

        Assert.True(vm.LacksBundledServices);
    }

    [Fact]
    public void The_android_host_points_the_wallet_at_the_bundled_programs()
    {
        var tor = Path.Combine(_directory, "libTor.so");
        var monero = Path.Combine(_directory, "libmonero-wallet-rpc.so");
        File.WriteAllText(tor, "");
        File.WriteAllText(monero, "");

        EmbeddedTorService.ExecutableOverride = tor;
        MoneroRpcService.ExecutableOverride = monero;

        Assert.Equal(tor, EmbeddedTorService.TorExecutablePath);
        Assert.True(EmbeddedTorService.IsBundlePresent);
        Assert.Equal(monero, MoneroRpcService.ExecutablePath);
        Assert.True(MoneroRpcService.IsBundlePresent);
    }

    [Fact]
    public void A_leftover_program_is_recognised_by_the_name_it_runs_under()
    {
        // Windows drops the extension from a process name; Linux and Android keep the whole file name,
        // so a leftover phone Tor runs as "libTor.so" and the desktop one as "tor".
        var expected = OperatingSystem.IsWindows() ? "libTor" : "libTor.so";
        Assert.Equal(expected, EmbeddedTorService.ProcessNameOf("/data/app/x/lib/arm64/libTor.so"));
        Assert.Equal("tor", EmbeddedTorService.ProcessNameOf(Path.Combine("tor", OperatingSystem.IsWindows() ? "tor.exe" : "tor")));
    }

    private static void Settings(string json)
    {
        var withConsent = json.TrimEnd('}') +
            $$""","Language":"en","AcceptedTermsVersion":{{FirstRunConsent.CurrentVersion}}}""";
        File.WriteAllText(Path.Combine(AppPaths.DataRoot, "ui-settings.json"), withConsent);
    }
}

/// <summary>
/// The balance chart values today's holdings across the chosen window, so it needs the price history of
/// each coin held — and asking a price server for exactly those coins told it what the wallet holds. The
/// wallet asks for the whole market list instead, in one fixed order, the same from every copy.
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class ChartHistoryPrivacyTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"umbrella-chart-{Guid.NewGuid():N}");

    public ChartHistoryPrivacyTests() => MainViewModel.FetchCurrencyOnStart = false;

    public void Dispose()
    {
        MainViewModel.FetchCurrencyOnStart = true;
        TestDataIsolation.RestoreBaselineSettings();
        TestDataIsolation.GoOffline();
    }

    [Fact]
    public void The_history_asked_for_is_the_whole_market_list_in_a_fixed_order_whatever_is_held()
    {
        var empty = new MainViewModel(new EncryptedFileSeedVault(Path.Combine(_directory, "a.json")));
        var holder = new MainViewModel(new EncryptedFileSeedVault(Path.Combine(_directory, "b.json")));
        holder.WatchAddresses.Add(new WatchAddress("ETH", "0x0000000000000000000000000000000000000001", "Cold"));

        var asked = holder.MarketSeriesSymbols();

        Assert.Equal(empty.MarketSeriesSymbols(), asked);
        Assert.Equal(asked.OrderBy(s => s, StringComparer.Ordinal), asked);
        Assert.Equal(holder.Market.Select(m => m.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).Count(), asked.Count);
        Assert.True(asked.Count > 10);
    }
}
