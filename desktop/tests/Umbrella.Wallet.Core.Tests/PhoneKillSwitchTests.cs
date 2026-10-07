using Umbrella.Wallet.App;
using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.Core.Safety;
using Umbrella.Wallet.Infrastructure;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The phone has no bundled Tor, and its start-up used to settle that by switching the kill-switch off
/// at every launch — including for someone routing through Orbot, the one setup where the switch is
/// exactly what stops the wallet going direct when Orbot is stopped. It also switched it off only AFTER
/// arming the transport, so the session refused every request while the setting read "off".
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class PhoneKillSwitchTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"umbrella-phone-{Guid.NewGuid():N}");

    public PhoneKillSwitchTests()
    {
        MainViewModel.HasBundledServices = false;
        MainViewModel.FetchCurrencyOnStart = false;
    }

    public void Dispose()
    {
        MainViewModel.HasBundledServices = !OperatingSystem.IsAndroid();
        MainViewModel.FetchCurrencyOnStart = true;
        TestDataIsolation.RestoreBaselineSettings();
        TestDataIsolation.GoOffline();
    }

    [Fact]
    public void With_orbot_as_the_proxy_the_kill_switch_survives_a_restart()
    {
        // Port 1: nothing listens, so even an armed request leaves this machine for nowhere.
        Settings("""{"TorOnlyMode":true,"TorEnabled":true,"CustomProxyEnabled":true,"CustomProxyUri":"127.0.0.1:1"}""");

        var vm = NewViewModel();

        Assert.True(vm.TorOnly);
        Assert.False(vm.TorEnabled);                       // no bundled Tor to claim
        Assert.True(PublicHttp.RequireProxy);
        Assert.Equal("socks5://127.0.0.1:1", PublicHttp.ActiveProxy);
        Assert.Equal(Loc.Instance["priv.torOnlyProxy"], vm.TorOnlyStatus);
    }

    [Fact]
    public void Without_a_proxy_the_switch_and_the_transport_agree_it_is_off()
    {
        Settings("""{"TorOnlyMode":true,"TorEnabled":true}""");

        var vm = NewViewModel();
        var (setting, transport) = (vm.TorOnly, PublicHttp.RequireProxy);
        TestDataIsolation.GoOffline();   // back behind the wall before anything else can run

        Assert.False(setting);
        Assert.False(transport);
    }

    private MainViewModel NewViewModel() =>
        new(new EncryptedFileSeedVault(Path.Combine(_directory, "vault.json")));

    private static void Settings(string json)
    {
        var withConsent = json.TrimEnd('}') +
            $$""","Language":"en","AcceptedTermsVersion":{{FirstRunConsent.CurrentVersion}}}""";
        File.WriteAllText(Path.Combine(AppPaths.DataRoot, "ui-settings.json"), withConsent);
    }
}
