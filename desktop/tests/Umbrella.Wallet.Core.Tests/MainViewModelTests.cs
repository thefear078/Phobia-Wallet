using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Drives the same commands the buttons are bound to.
///
/// Reported as "the phrase is not generated when you create a wallet". Generation was never
/// broken — the form rejected the password and wrote the reason to StatusMessage in the title
/// bar, so the button looked dead. Every section then looked dead too, because they are gated
/// behind ShowWorkspace => IsUnlocked. These tests pin both the happy path and the feedback.
/// </summary>
public sealed class MainViewModelTests : IDisposable
{
    private const string GoodPassword = "umbrella-test-vault-2026";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"umbrella-vm-{Guid.NewGuid():N}");

    private MainViewModel NewViewModel() =>
        new(new EncryptedFileSeedVault(Path.Combine(_directory, "vault.json")));

    [Fact]
    public async Task CreateWallet_ShowsA24WordPhrase_AndOpensTheWorkspace()
    {
        var vm = NewViewModel();
        vm.Password = GoodPassword;
        vm.ConfirmPassword = GoodPassword;

        await vm.CreateWalletCommand.ExecuteAsync(null);

        // Create shows the phrase-backup page first; the workspace opens only after ack.
        Assert.Equal(24, vm.RecoveryPhrase.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.True(vm.IsUnlocked);
        Assert.True(vm.IsBackupStage);
        Assert.False(vm.IsWorkspace);
        Assert.False(vm.IsWelcomeStage);
        Assert.Empty(vm.FormError);

        vm.ConfirmPhraseBackupCommand.Execute(null);
        Assert.True(vm.IsWorkspace);
        Assert.False(vm.IsBackupStage);
        Assert.Empty(vm.RecoveryPhrase);
    }

    [Fact]
    public async Task CreateWallet_ShortPassword_ReportsVisibly_AndGeneratesNothing()
    {
        var vm = NewViewModel();
        vm.Password = "short";
        vm.ConfirmPassword = "short";

        await vm.CreateWalletCommand.ExecuteAsync(null);

        // The regression: this used to be reachable only via StatusMessage in the title bar.
        Assert.True(vm.HasFormError);
        Assert.Contains("12", vm.FormError);
        Assert.Empty(vm.RecoveryPhrase);
        Assert.False(vm.IsUnlocked);
    }

    [Fact]
    public async Task CreateWallet_MismatchedConfirm_ReportsVisibly()
    {
        var vm = NewViewModel();
        vm.Password = GoodPassword;
        vm.ConfirmPassword = GoodPassword + "-typo";

        await vm.CreateWalletCommand.ExecuteAsync(null);

        Assert.True(vm.HasFormError);
        Assert.Contains("do not match", vm.FormError);
        Assert.Empty(vm.RecoveryPhrase);
    }

    [Fact]
    public void TypingAPassword_ClearsTheError_AndMovesTheMeter()
    {
        var vm = NewViewModel();
        vm.FormError = "stale error";

        vm.Password = GoodPassword;

        Assert.False(vm.HasFormError);
        Assert.Contains("strong", vm.PasswordMeterLabel);
        Assert.True(vm.CanSubmitVaultForm);
    }

    [Fact]
    public async Task Sections_TrackTheNavButtons()
    {
        var vm = NewViewModel();
        vm.Password = GoodPassword;
        vm.ConfirmPassword = GoodPassword;
        await vm.CreateWalletCommand.ExecuteAsync(null);

        vm.SelectSectionCommand.Execute("Settings");
        Assert.True(vm.IsSettings);
        Assert.False(vm.IsPortfolio);

        vm.SelectSectionCommand.Execute("Send");
        Assert.True(vm.IsSend);
        Assert.False(vm.IsSettings);
    }

    /// <summary>With no vault, the app shows the full-screen Welcome, not the workspace.</summary>
    [Fact]
    public void NoVault_ShowsWelcome_NotWorkspace()
    {
        var vm = NewViewModel();

        Assert.True(vm.IsWelcomeStage);
        Assert.False(vm.ShowSidebar);
        Assert.False(vm.IsWorkspace);

        vm.GoToImportCommand.Execute(null);
        Assert.True(vm.IsImportStage);
        Assert.False(vm.IsWelcomeStage);

        vm.GoToWelcomeCommand.Execute(null);
        Assert.True(vm.IsWelcomeStage);
    }

    /// <summary>The market list is populated at startup and states support honestly.</summary>
    [Fact]
    public void Market_ListsEveryCoin_WithHonestSupport()
    {
        var vm = NewViewModel();

        Assert.Equal(Chains.ChainCatalog.All.Count, vm.Market.Count);
        Assert.Contains(vm.Market, m => m.Symbol == "BTC" && m.IsSupported);
        Assert.Contains(vm.Market, m => m.Symbol == "SOL" && m.IsSupported);
        Assert.Contains(vm.Market, m => m.Symbol == "XMR" && !m.IsSupported);
    }

    /// <summary>Reveal must never expose the phrase without the correct password.</summary>
    [Fact]
    public async Task RevealInSettings_NeverShowsPhraseWithoutPassword()
    {
        var vm = NewViewModel();
        vm.Password = GoodPassword;
        vm.ConfirmPassword = GoodPassword;
        await vm.CreateWalletCommand.ExecuteAsync(null);
        vm.ConfirmPhraseBackupCommand.Execute(null);

        // No password entered.
        vm.SettingsPassword = string.Empty;
        await vm.RevealPhraseCommand.ExecuteAsync(null);
        Assert.False(vm.IsSettingsPhraseVisible);
        Assert.Empty(vm.SettingsRevealedPhrase);

        // Wrong password.
        vm.SettingsPassword = "wrong-password-xxxx";
        await vm.RevealPhraseCommand.ExecuteAsync(null);
        Assert.False(vm.IsSettingsPhraseVisible);
        Assert.Empty(vm.SettingsRevealedPhrase);

        // Correct password.
        vm.SettingsPassword = GoodPassword;
        await vm.RevealPhraseCommand.ExecuteAsync(null);
        Assert.True(vm.IsSettingsPhraseVisible);
        Assert.Equal(24, vm.SettingsRevealedPhrase.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);

        // Leaving Settings clears it from the screen.
        vm.SelectSectionCommand.Execute("Portfolio");
        Assert.False(vm.IsSettingsPhraseVisible);
        Assert.Empty(vm.SettingsRevealedPhrase);
    }

    /// <summary>
    /// Send is real for ETH/BTC/LTC/SOL. Unsupported assets and malformed destinations must be
    /// refused before anything is signed, and neither case may write a fake Activity row.
    /// </summary>
    [Fact]
    public async Task Send_RefusesUnsupportedAssetsAndBadAddresses_WithoutFakeActivity()
    {
        var vm = NewViewModel();
        vm.Password = GoodPassword;
        vm.ConfirmPassword = GoodPassword;
        await vm.CreateWalletCommand.ExecuteAsync(null);
        vm.ConfirmPhraseBackupCommand.Execute(null);

        var activityBefore = vm.Activity.Count;

        // XMR can be sent, but only once the bundled Monero daemon is running — with it off,
        // the user must be told to turn it on rather than silently getting nothing.
        vm.SendChain = "XMR";
        vm.SendTo = "44AFFq5kSiGBoZ4NMDwYtN18obc8AemS33DBLWs3H7otXft3XjrpDtQGv7SqSsaBYBb98uNbr2VBBEt7f2wfn3RVGQBEP3A";
        vm.SendAmount = "0.01";
        await vm.PrepareSendCommand.ExecuteAsync(null);
        Assert.Contains("Monero wallet service", vm.SendError);
        Assert.False(vm.HasSendQuote);

        // A chain with no derivation at all is still refused outright.
        vm.SendChain = "TON";
        vm.SendTo = "EQCD39VS5jcptHL8vMjEXrzGaRcCVYto7HUn4bpAOg8xqB2N";
        await vm.PrepareSendCommand.ExecuteAsync(null);
        Assert.Contains("not available", vm.SendError);
        Assert.False(vm.HasSendQuote);

        // ETH with a malformed destination → rejected before any signing.
        vm.SendChain = "ETH";
        vm.SendTo = "not-an-address";
        await vm.PrepareSendCommand.ExecuteAsync(null);
        Assert.Contains("0x", vm.SendError);
        Assert.False(vm.HasSendQuote);

        // BTC with a destination that isn't a valid mainnet address → rejected too.
        vm.SendChain = "BTC";
        vm.SendTo = "definitely-not-bitcoin";
        await vm.PrepareSendCommand.ExecuteAsync(null);
        Assert.False(vm.HasSendQuote);
        Assert.NotEmpty(vm.SendError);

        // Confirm without a quote must refuse.
        await vm.ConfirmSendCommand.ExecuteAsync(null);
        Assert.Contains("Prepare", vm.SendError);

        Assert.Equal(activityBefore, vm.Activity.Count);
        Assert.DoesNotContain(vm.Activity, a => a.Kind.Contains("Sen", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeleteVault_RequiresTypedConfirmation()
    {
        var vm = NewViewModel();
        vm.Password = GoodPassword;
        vm.ConfirmPassword = GoodPassword;
        await vm.CreateWalletCommand.ExecuteAsync(null);

        vm.DeleteConfirmation = "yes";
        vm.DeleteVaultCommand.Execute(null);
        Assert.True(vm.HasVault);

        vm.DeleteConfirmation = "DELETE";
        vm.DeleteVaultCommand.Execute(null);
        Assert.False(vm.HasVault);
        Assert.True(vm.IsWelcomeStage);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
