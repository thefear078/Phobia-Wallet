using Umbrella.Wallet.App;
using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The amount box and the password that guards the send password.
///
/// On 2026-10-10 the Send screen had two amount boxes, the currency one on top with only a grey
/// watermark to say so. A number meant as SOL was typed into it and went out as that many units of the
/// display currency — a thousand times less, under a fee several times the amount — and then again. One
/// box now; these pin what it takes, what it shows, and that changing the unit never changes the amount.
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class SendSheetTests : IDisposable
{
    private const string GoodPassword = "umbrella-test-vault-2026";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"umbrella-sendsheet-{Guid.NewGuid():N}");

    public void Dispose()
    {
        TestDataIsolation.RestoreBaselineSettings();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private MainViewModel NewViewModel()
    {
        TestDataIsolation.RestoreBaselineSettings();
        return new(new EncryptedFileSeedVault(Path.Combine(_directory, "vault.json")));
    }

    /// <summary>A view model holding 2 SOL at 100 dollars each, with SOL chosen on the Send screen.</summary>
    private MainViewModel WithSolana(bool priced = true)
    {
        var vm = NewViewModel();
        vm.Accounts.Clear();
        vm.Accounts.Add(new WalletAccountViewModel("SOL", "SOL", "Ready",
            "9WzDXwBbmkg8ZTbNMqUxvQRAyrZzDsGYdLVL9zYtAWWM", "SLIP10", 100, 2, "Solana", 0, Balance: BalanceRead.Live));
        vm.SetHoldingsSortCommand.Execute("Name");
        if (priced) vm.SetPricesForTest(new Dictionary<string, (decimal, decimal)> { ["SOL"] = (100m, 0m) });
        vm.SelectedSendAsset = vm.SendableAssetOptions.Single(o => o.Symbol == "SOL");
        return vm;
    }

    [Fact]
    public void The_box_takes_the_coin_unless_told_otherwise_and_says_so()
    {
        var vm = WithSolana();

        Assert.False(vm.SendAmountInFiat);
        Assert.Equal("SOL", vm.SendAmountUnit);

        vm.SendAmount = "0.001";

        Assert.Equal("0.001", vm.SendAmount);   // a thousandth of a coin, not of a currency
    }

    [Fact]
    public void In_currency_mode_the_box_names_the_currency_and_fills_in_the_coin()
    {
        var vm = WithSolana();

        vm.ToggleSendAmountUnitCommand.Execute(null);
        vm.SendFiatAmount = (0.5m * Fx.Rate).ToString(System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(vm.SendAmountInFiat);
        Assert.Equal(Fx.Code, vm.SendAmountUnit);
        Assert.Equal("0.005", vm.SendAmount);                 // half a dollar of a 100-dollar coin
        Assert.Equal("0.005 SOL", vm.SendWillSendCoin);       // and the line under the box says so, in the coin
    }

    [Fact]
    public void Changing_the_unit_never_changes_the_amount()
    {
        var vm = WithSolana();
        vm.SendAmount = "0.00123456";

        vm.ToggleSendAmountUnitCommand.Execute(null);

        // The currency box shows what the coin amount is worth (rounded to cents) — and that rounded
        // figure is NOT read back as a new amount.
        Assert.Equal("0.00123456", vm.SendAmount);
        Assert.False(string.IsNullOrEmpty(vm.SendFiatAmount));

        vm.ToggleSendAmountUnitCommand.Execute(null);

        Assert.False(vm.SendAmountInFiat);
        Assert.Equal("0.00123456", vm.SendAmount);
    }

    [Fact]
    public void Max_and_the_percent_presets_write_the_coin_and_show_the_coin()
    {
        var vm = WithSolana();
        vm.ToggleSendAmountUnitCommand.Execute(null);
        Assert.True(vm.SendAmountInFiat);

        vm.SetAmountPercentCommand.Execute("50");

        Assert.False(vm.SendAmountInFiat);
        Assert.Equal("SOL", vm.SendAmountUnit);
        Assert.Equal("1", vm.SendAmount);

        vm.ToggleSendAmountUnitCommand.Execute(null);
        vm.SetMaxAmountCommand.Execute(null);

        Assert.False(vm.SendAmountInFiat);
    }

    [Fact]
    public void Without_a_price_there_is_no_currency_mode_to_switch_to()
    {
        var vm = WithSolana(priced: false);

        vm.ToggleSendAmountUnitCommand.Execute(null);

        Assert.False(vm.FiatInputAvailable);
        Assert.False(vm.SendAmountInFiat);
        Assert.Equal("SOL", vm.SendAmountUnit);
    }

    [Fact]
    public void Choosing_another_asset_puts_the_box_back_in_that_assets_own_unit()
    {
        var vm = WithSolana();
        vm.Accounts.Add(new WalletAccountViewModel("ETH", "ETH", "Ready",
            "0x9858EfFD232B4033E47d90003D41EC34EcaEda94", "BIP44", 2000, 1, "Ethereum", 0, Balance: BalanceRead.Live));
        vm.SetHoldingsSortCommand.Execute("Name");
        vm.SetPricesForTest(new Dictionary<string, (decimal, decimal)> { ["SOL"] = (100m, 0m), ["ETH"] = (2000m, 0m) });
        // Whichever of the two the picker is on (it opens on the larger holding), switch to the other.
        var other = vm.SelectedSendAsset!.Symbol == "SOL" ? "ETH" : "SOL";
        vm.ToggleSendAmountUnitCommand.Execute(null);
        Assert.True(vm.SendAmountInFiat);

        vm.SelectedSendAsset = vm.SendableAssetOptions.Single(o => o.Symbol == other);

        Assert.False(vm.SendAmountInFiat);
        Assert.Equal(other, vm.SendAmountUnit);
    }

    [Fact]
    public void An_amount_worth_less_than_a_cent_is_not_shown_as_worth_nothing()
    {
        var vm = WithSolana();

        vm.SendAmount = "0.000001";

        Assert.StartsWith("<", vm.SendAmountFiat);            // "< $0.01", never "≈ $0.00"
        vm.SendAmount = "1";
        Assert.StartsWith("≈", vm.SendAmountFiat);
    }

    // ---- The password that guards the send password ------------------------------------------------

    private async Task<MainViewModel> UnlockedAsync()
    {
        var vm = NewViewModel();
        vm.Password = GoodPassword;
        vm.ConfirmPassword = GoodPassword;
        await vm.CreateWalletCommand.ExecuteAsync(null);
        vm.ConfirmPhraseBackupCommand.Execute(null);
        vm.RequirePasswordForSend = true;
        return vm;
    }

    [Fact]
    public async Task Switching_the_send_password_off_asks_for_the_password_first()
    {
        var vm = await UnlockedAsync();

        vm.RequirePasswordForSendSwitch = false;

        Assert.True(vm.RequirePasswordForSend);               // still on
        Assert.True(vm.RequirePasswordForSendSwitch);          // and the switch says so
        Assert.True(vm.SendPasswordOffAsked);
    }

    [Fact]
    public async Task A_wrong_password_leaves_it_on()
    {
        var vm = await UnlockedAsync();
        vm.RequirePasswordForSendSwitch = false;

        vm.SendPasswordOffInput = "not-the-password";
        await vm.ConfirmSendPasswordOffCommand.ExecuteAsync(null);

        Assert.True(vm.RequirePasswordForSend);
        Assert.True(vm.HasSendPasswordOffError);
        Assert.Equal(string.Empty, vm.SendPasswordOffInput);   // what was typed is not kept on screen
    }

    [Fact]
    public async Task No_password_at_all_leaves_it_on()
    {
        var vm = await UnlockedAsync();
        vm.RequirePasswordForSendSwitch = false;

        await vm.ConfirmSendPasswordOffCommand.ExecuteAsync(null);

        Assert.True(vm.RequirePasswordForSend);
        Assert.True(vm.HasSendPasswordOffError);
    }

    [Fact]
    public async Task The_right_password_switches_it_off_and_switching_it_back_on_is_free()
    {
        var vm = await UnlockedAsync();
        vm.RequirePasswordForSendSwitch = false;

        vm.SendPasswordOffInput = GoodPassword;
        await vm.ConfirmSendPasswordOffCommand.ExecuteAsync(null);

        Assert.False(vm.RequirePasswordForSend);
        Assert.False(vm.RequirePasswordForSendSwitch);
        Assert.False(vm.SendPasswordOffAsked);

        vm.RequirePasswordForSendSwitch = true;                // no password needed to be safer

        Assert.True(vm.RequirePasswordForSend);
        Assert.False(vm.SendPasswordOffAsked);
    }

    [Fact]
    public async Task Cancelling_leaves_it_on_and_puts_the_question_away()
    {
        var vm = await UnlockedAsync();
        vm.RequirePasswordForSendSwitch = false;
        vm.SendPasswordOffInput = "half-typed";

        vm.CancelSendPasswordOffCommand.Execute(null);

        Assert.True(vm.RequirePasswordForSend);
        Assert.False(vm.SendPasswordOffAsked);
        Assert.Equal(string.Empty, vm.SendPasswordOffInput);
    }

    // ---- The receipt -------------------------------------------------------------------------------

    [Fact]
    public void A_receipt_copies_as_plain_text_with_everything_needed_to_look_it_up()
    {
        var receipt = new SendReceiptVm(SendReceiptState.Confirmed, "SOL", "0.001 SOL", "≈ $0.10",
            "DestinationAddressUsedOnlyInThisTest11111111", "TransactionIdUsedOnlyInThisTest", "https://solscan.io/tx/TransactionIdUsedOnlyInThisTest",
            "0.000005 SOL", "10 Oct 2026 · 00:17", string.Empty);

        var text = receipt.AsText();

        Assert.Contains("0.001 SOL", text);
        Assert.Contains("DestinationAddressUsedOnlyInThisTest11111111", text);   // the whole address
        Assert.Contains("TransactionIdUsedOnlyInThisTest", text);                  // the whole id
        Assert.Contains("https://solscan.io/tx/TransactionIdUsedOnlyInThisTest", text);
        Assert.Contains("0.000005 SOL", text);
    }

    [Fact]
    public void Only_a_followed_send_is_called_confirmed()
    {
        static SendReceiptVm With(SendReceiptState state) =>
            new(state, "BTC", "1 BTC", "", "bc1q", "txid", "", "", "now", "");

        Assert.True(With(SendReceiptState.Confirmed).IsConfirmed);
        Assert.False(With(SendReceiptState.Broadcast).IsConfirmed);
        Assert.False(With(SendReceiptState.Unclear).IsConfirmed);
        Assert.True(With(SendReceiptState.Unclear).IsUnclear);
        // Three endings, three different things said — never one word for all of them.
        Assert.Equal(3, new[] { SendReceiptState.Confirmed, SendReceiptState.Broadcast, SendReceiptState.Unclear }
            .Select(s => With(s).StatusText).Distinct().Count());
    }
}
