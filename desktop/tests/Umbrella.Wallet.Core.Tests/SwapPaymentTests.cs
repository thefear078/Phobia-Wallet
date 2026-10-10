using Umbrella.Wallet.App;
using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.Infrastructure;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// A swap is paid exactly as it was quoted, or not at all.
///
/// On 2026-10-10 a swap of a whole balance could not be paid — "Max" on the Swap screen had left nothing
/// for the network fee. "Max" on the Send screen then put in a smaller amount, the payment went to the
/// route's deposit address short of its quote, and the route held it until its deadline: the coins left,
/// nothing came back, and the row said "confirmed". These pin the three places that let it happen, and
/// the reading of the route's answer that says what happened.
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class SwapPaymentTests : IDisposable
{
    private const string GoodPassword = "umbrella-test-vault-2026";
    private const string Deposit = "DepositAddressOfTheRouteUsedOnlyInTests11111";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"umbrella-swap-{Guid.NewGuid():N}");

    public void Dispose()
    {
        TestDataIsolation.RestoreBaselineSettings();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    /// <summary>An unlocked wallet holding 0.5 SOL.</summary>
    private async Task<MainViewModel> WithSolAsync(double sol = 0.5)
    {
        TestDataIsolation.RestoreBaselineSettings();
        var vm = new MainViewModel(new EncryptedFileSeedVault(Path.Combine(_directory, "vault.json")));
        vm.Password = GoodPassword;
        vm.ConfirmPassword = GoodPassword;
        await vm.CreateWalletCommand.ExecuteAsync(null);
        vm.ConfirmPhraseBackupCommand.Execute(null);
        await BackgroundRefresh.SettleAsync(vm);

        var row = vm.Accounts.First(a => a.Symbol == "SOL");
        vm.Accounts[vm.Accounts.IndexOf(row)] = row with { Amount = sol, Price = 100, Balance = BalanceRead.Live };
        vm.RecomputeHoldingsForTest();
        vm.SelectedSendAsset = vm.SendableAssetOptions.Single(o => o.Symbol == "SOL");
        return vm;
    }

    // ---- "Max" on the Swap screen ----------------------------------------------------------------------

    [Fact]
    public async Task Max_leaves_what_the_payments_own_fee_needs()
    {
        var vm = await WithSolAsync(0.5);
        vm.SwapFromAsset = SwapCatalog.Find("SOL");

        vm.SwapUseBalanceCommand.Execute(null);

        var reserve = MainViewModel.SendMaxReserveFor("SOL");
        Assert.True(reserve > 0);
        Assert.Equal((0.5m - reserve).ToString("0.########", System.Globalization.CultureInfo.InvariantCulture), vm.SwapAmount);
    }

    [Fact]
    public async Task A_swap_of_more_than_can_be_paid_is_refused_before_any_route_is_asked()
    {
        var vm = await WithSolAsync(0.5);
        vm.SwapFromAsset = SwapCatalog.Find("SOL");
        vm.SwapToAsset = SwapCatalog.Find("ETH");
        vm.SwapAmount = "0.5";   // the whole balance: nothing left for the fee

        await vm.GetSwapQuoteCommand.ExecuteAsync(null);

        Assert.False(vm.HasSwapQuote);
        Assert.Contains("SOL", vm.SwapError);
        Assert.Contains("0.498", vm.SwapError);   // what CAN be swapped: the balance less the fee reserve
    }

    // ---- the payment on the Send screen -------------------------------------------------------------------

    [Fact]
    public async Task A_swaps_payment_cannot_be_reviewed_for_another_amount()
    {
        var vm = await WithSolAsync();
        vm.SetSwapPaymentForTest("SOL", "ETH", 0.25m, Deposit);
        Assert.True(vm.HasSwapPayment);

        vm.SendAmount = "0.2";   // "a little less, so the fee fits"
        await vm.PrepareSendCommand.ExecuteAsync(null);

        Assert.False(vm.HasSendQuote);                       // nothing to confirm
        Assert.Equal("0.25", vm.SendAmount);                 // the quoted amount is put back
        Assert.Contains("0.25", vm.SendError);               // and the reason says it
    }

    [Fact]
    public async Task Max_and_the_presets_do_not_touch_a_swaps_payment()
    {
        var vm = await WithSolAsync();
        vm.SetSwapPaymentForTest("SOL", "ETH", 0.25m, Deposit);

        vm.SetMaxAmountCommand.Execute(null);
        Assert.Equal("0.25", vm.SendAmount);
        Assert.False(string.IsNullOrEmpty(vm.SendError));

        vm.SendError = string.Empty;
        vm.SetAmountPercentCommand.Execute("50");
        Assert.Equal("0.25", vm.SendAmount);
        Assert.False(string.IsNullOrEmpty(vm.SendError));

        vm.ToggleSendAmountUnitCommand.Execute(null);
        Assert.False(vm.SendAmountInFiat);
    }

    [Fact]
    public async Task Another_destination_is_an_ordinary_send_again()
    {
        var vm = await WithSolAsync();
        vm.SetSwapPaymentForTest("SOL", "ETH", 0.25m, Deposit);

        vm.SendTo = "9xQeWvG816bUx9EPjHmaT23yvVM2ZWbrrpZb9PusVFin";

        Assert.False(vm.HasSwapPayment);   // the banner, and the fixed amount, belong to the route's address only
        vm.SetAmountPercentCommand.Execute("50");
        Assert.Equal("0.25", vm.SendAmount);   // half of 0.5 SOL — computed, not refused
        Assert.True(string.IsNullOrEmpty(vm.SendError));
    }

    // ---- what the route says -----------------------------------------------------------------------------

    /// <summary>The shape of a real answer to a short deposit, with made-up figures.</summary>
    private const string ShortDeposit = """
        {"status":"INCOMPLETE_DEPOSIT","updatedAt":"2026-01-01T10:01:00.000Z",
         "swapDetails":{"depositedAmount":"3000000","depositedAmountFormatted":"0.003","refundedAmountFormatted":"0",
                        "amountInFormatted":null,"amountOutFormatted":null},
         "quoteResponse":{"quote":{"amountInFormatted":"0.005","amountOutFormatted":"0.00019","deadline":"2026-01-04T11:00:00.000Z"},
                          "quoteRequest":{"deadline":"2026-01-01T11:00:00.000Z","refundType":"ORIGIN_CHAIN"}}}
        """;

    [Fact]
    public void A_short_deposit_is_read_with_what_arrived_what_was_quoted_and_until_when()
    {
        var status = NearIntentsSwapClient.ParseStatus(ShortDeposit);

        Assert.NotNull(status);
        Assert.Equal("INCOMPLETE_DEPOSIT", status!.Status);
        Assert.Equal("0.003", status.Deposited);
        Assert.Equal("0.005", status.Quoted);
        // The deadline of the REQUEST — when a short payment starts being refunded — not the quote's
        // own, which is days later and only says how long the deposit address exists.
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 11, 0, 0, TimeSpan.Zero), status.Deadline);
        Assert.Equal(("swap.st.incomplete", false), MainViewModel.SwapStatusKey(status.Status));
    }

    [Fact]
    public void A_finished_swap_is_read_with_what_was_delivered()
    {
        var status = NearIntentsSwapClient.ParseStatus(
            """{"status":"SUCCESS","swapDetails":{"amountOutFormatted":"0.00019","depositedAmountFormatted":"0.005"}}""");

        Assert.Equal("0.00019", status!.Delivered);
        Assert.Equal(("swap.st.done", true), MainViewModel.SwapStatusKey(status.Status));
    }

    [Fact]
    public void An_answer_without_a_state_is_no_answer()
    {
        Assert.Null(NearIntentsSwapClient.ParseStatus("""{"message":"not found"}"""));
        Assert.Null(NearIntentsSwapClient.ParseStatus("[]"));
    }

    // ---- following ------------------------------------------------------------------------------------------

    [Fact]
    public void A_swap_is_looked_at_every_few_seconds_at_first_then_every_half_minute()
    {
        var first = Enumerable.Range(0, 7).Sum(i => MainViewModel.SwapPollDelay(i).TotalSeconds);

        Assert.Equal(6, MainViewModel.SwapPollDelay(0).TotalSeconds);
        Assert.True(first <= 80);                                         // seven looks in the first minute and a bit
        Assert.Equal(30, MainViewModel.SwapPollDelay(7).TotalSeconds);
        Assert.Equal(30, MainViewModel.SwapPollDelay(200).TotalSeconds);
    }

    [Fact]
    public void Paid_swaps_are_kept_so_they_can_be_followed_after_a_restart()
    {
        var path = Path.Combine(_directory, "swaps.json");
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var store = new SwapStore(path);
        store.Upsert(new SwapStore.Entry("w1", "NearIntents", "SOL", "ETH", 0.25m, 0.01m, Deposit, null, null, "tx1", now));
        store.Upsert(new SwapStore.Entry("w2", "Exolix", "BTC", "XMR", 0.01m, 2m, "other", null, "order-1", "tx2", now - 5));

        var again = new SwapStore(path).Load();
        Assert.Equal(new[] { "w1", "w2" }, again.Select(e => e.WalletId));   // newest first
        Assert.False(again[0].Ended);

        // The same swap, now ended: replaced, not added.
        store.Upsert(again[0] with { Status = "REFUNDED", Ended = true });
        var ended = new SwapStore(path).Load();
        Assert.Equal(2, ended.Count);
        Assert.True(ended.Single(e => e.WalletId == "w1").Ended);

        store.Clear();
        Assert.Empty(new SwapStore(path).Load());
    }

    // ---- the balance after a send ---------------------------------------------------------------------------

    [Fact]
    public void A_reading_that_has_not_seen_the_send_does_not_put_the_money_back()
    {
        // 1.0 before, 0.3 sent (fee included). The explorer still answers 1.0.
        var (shown, caughtUp) = BalanceReadout.LessUnseenDebit(read: 1.0, before: 1.0, debit: 0.3);

        Assert.Equal(0.7, shown, 9);
        Assert.False(caughtUp);
    }

    [Fact]
    public void Once_the_reading_shows_the_send_it_stands_as_it_is()
    {
        // The explorer has caught up — and the fee turned out a little smaller than reserved.
        var (shown, caughtUp) = BalanceReadout.LessUnseenDebit(read: 0.7004, before: 1.0, debit: 0.3);

        Assert.Equal(0.7004, shown, 9);
        Assert.True(caughtUp);
    }

    [Fact]
    public void Money_arriving_meanwhile_is_not_swallowed_by_the_pending_send()
    {
        // 0.5 arrived while the send of 0.3 was still unseen: reading 1.5, shown 1.2.
        var (shown, caughtUp) = BalanceReadout.LessUnseenDebit(read: 1.5, before: 1.0, debit: 0.3);

        Assert.Equal(1.2, shown, 9);
        Assert.False(caughtUp);
    }

    [Fact]
    public void It_never_shows_less_than_nothing()
    {
        Assert.Equal(0, BalanceReadout.LessUnseenDebit(read: 0.1, before: 0.1, debit: 0.3).Shown);
    }
}
