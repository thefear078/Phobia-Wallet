using Umbrella.Wallet.App;
using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The wallet's memory of who it has paid, and the user's own words on their history.
///
/// The Send screen said "first send to this address" about an address paid the day before — every time.
/// Activity rows kept the other side as "abcd…wxyz" only, which equals no address and resembles none, so
/// neither "you have paid this before" nor the look-alike warning could ever fire from history. Rows now
/// keep the whole address; these pin what that makes possible, and the notes and names written on them.
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class TransactionNotesAndNamesTests : IDisposable
{
    private const string GoodPassword = "umbrella-test-vault-2026";

    // Made-up addresses of the right shape: a recipient, and its poisoned twin (same head and tail).
    private const string Paid = "9xQeWvG816bUx9EPjHmaT23yvVM2ZWbrrpZb9PusVFin";
    private const string Twin = "9xQeWv111111111111111111111111111111111sVFin";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"umbrella-notes-{Guid.NewGuid():N}");

    public void Dispose()
    {
        TestDataIsolation.RestoreBaselineSettings();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private async Task<MainViewModel> UnlockedAsync()
    {
        TestDataIsolation.RestoreBaselineSettings();
        var vm = new MainViewModel(new EncryptedFileSeedVault(Path.Combine(_directory, "vault.json")));
        vm.Password = GoodPassword;
        vm.ConfirmPassword = GoodPassword;
        await vm.CreateWalletCommand.ExecuteAsync(null);
        vm.ConfirmPhraseBackupCommand.Execute(null);
        await BackgroundRefresh.SettleAsync(vm);
        return vm;
    }

    private static ActivityRowViewModel Sent(string to, string txId = "5KtPn1LGuxhFiwjxErkxTb7XxtLVYUBe6Cn33ej9RPG9Yq", long ms = 1_700_000_000_000) =>
        new("Sent", "SOL", "-0.5", $"{to[..6]}…{to[^4..]}", "now", $"https://solscan.io/tx/{txId}", "Confirmed", ms,
            TxId: txId, CounterpartyFull: to);

    // ---- "first send to this address" ----------------------------------------------------------------

    [Fact]
    public async Task An_address_paid_before_is_recognised_not_called_a_first_send()
    {
        var vm = await UnlockedAsync();
        vm.Activity.Add(Sent(Paid));
        vm.SelectedSendAsset = vm.SendableAssetOptions.Single(o => o.Symbol == "SOL");

        vm.SendTo = Paid;

        Assert.True(vm.SendKnownContactShown);     // "you have sent to this address before"
        Assert.False(vm.SendSafetyShown);          // and no "first send" notice
    }

    [Fact]
    public async Task A_lookalike_of_an_address_paid_before_is_flagged_from_history()
    {
        var vm = await UnlockedAsync();
        vm.Activity.Add(Sent(Paid));
        vm.SelectedSendAsset = vm.SendableAssetOptions.Single(o => o.Symbol == "SOL");

        vm.SendTo = Twin;

        Assert.True(vm.SendSafetyShown);
        Assert.Equal(Loc.Instance["send.safetyPoisonTitle"], vm.SendSafetyTitle);
    }

    [Fact]
    public async Task A_failed_send_does_not_make_its_destination_a_known_one()
    {
        var vm = await UnlockedAsync();
        vm.Activity.Add(Sent(Paid) with { Status = "Failed" });
        vm.SelectedSendAsset = vm.SendableAssetOptions.Single(o => o.Symbol == "SOL");

        vm.SendTo = Paid;

        Assert.False(vm.SendKnownContactShown);    // nothing ever reached it
    }

    // ---- the row -------------------------------------------------------------------------------------

    [Theory]
    [InlineData("https://solscan.io/tx/5KtPn1LGuxhFiwjxErkxTb7XxtLVYUBe6Cn33ej9RPG9Yq", "5KtPn1LGuxhFiwjxErkxTb7XxtLVYUBe6Cn33ej9RPG9Yq")]
    [InlineData("https://mempool.space/tx/4a5e1e4baab89f3a32518a88c31bc87f618f76673e2cc77ab2127b7afdeda33b/", "4a5e1e4baab89f3a32518a88c31bc87f618f76673e2cc77ab2127b7afdeda33b")]
    [InlineData("https://etherscan.io/tx/0x5c504ed432cb51138bcf09aa5e8a410dd4a1e204ef84bfed1be16dfba1b22060?utm=x", "0x5c504ed432cb51138bcf09aa5e8a410dd4a1e204ef84bfed1be16dfba1b22060")]
    public void A_row_the_wallet_wrote_gets_its_transaction_id_from_its_link(string link, string id)
    {
        Assert.Equal(id, ActivityRowViewModel.TxIdFromExplorer(link));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://solscan.io/account")]
    public void A_link_that_is_not_a_transaction_gives_no_id(string? link)
    {
        Assert.Null(ActivityRowViewModel.TxIdFromExplorer(link));
    }

    [Fact]
    public void A_named_address_is_shown_by_its_name_with_the_address_beside_it()
    {
        var row = Sent(Paid) with { CounterpartyLabel = "Landlord" };

        Assert.Equal($"Landlord · {Paid[..6]}…{Paid[^4..]}", row.Detail);
        Assert.Equal($"{Paid[..6]}…{Paid[^4..]}", (row with { CounterpartyLabel = null }).Detail);
    }

    [Fact]
    public void Only_a_row_that_knows_the_whole_address_can_be_named()
    {
        Assert.True(Sent(Paid).CanLabel);
        Assert.False((Sent(Paid) with { CounterpartyFull = null }).CanLabel);
        Assert.True((Sent(Paid) with { CounterpartyFull = null }).CanAnnotate);   // it can still carry a note
        Assert.False(new ActivityRowViewModel("Security", "Vault", "unlocked", "this device", "now").CanAnnotate);
    }

    // ---- writing on a row ------------------------------------------------------------------------------

    [Fact]
    public async Task The_editor_opens_under_the_row_it_was_asked_for_and_only_that_one()
    {
        var vm = await UnlockedAsync();
        vm.Activity.Add(Sent(Paid, "5KtPn1LGuxhFiwjxErkxTb7XxtLVYUBe6Cn33ej9RPG9Yq", 1_700_000_000_000));
        vm.Activity.Add(Sent(Paid, "3vZ67CGoRYkuT76TtpP2VrtTPBfnvG2xj6mUTvvux46qbnpThgQDgm27nC3yQVUZrABFjT4xcBJKCHTnHvK2JN3V", 1_700_000_100_000));
        vm.ActivityFilter = "Transactions";
        var target = vm.FilteredActivity.Single(r => r.TxId!.StartsWith("3vZ6"));

        vm.BeginEditNoteCommand.Execute(target);

        Assert.True(vm.IsEditingNote);
        Assert.True(vm.FilteredActivity.Single(r => r.TxId!.StartsWith("3vZ6")).IsEditingNote);
        Assert.False(vm.FilteredActivity.Single(r => r.TxId!.StartsWith("5KtP")).IsEditingNote);

        // The same button closes it.
        vm.BeginEditNoteCommand.Execute(vm.FilteredActivity.Single(r => r.TxId!.StartsWith("3vZ6")));

        Assert.False(vm.IsEditingNote);
        Assert.DoesNotContain(vm.FilteredActivity, r => r.IsEditingNote);
    }

    [Fact]
    public async Task A_note_and_a_name_saved_from_the_row_show_on_every_row_they_belong_to()
    {
        var vm = await UnlockedAsync();
        vm.Activity.Add(Sent(Paid, "5KtPn1LGuxhFiwjxErkxTb7XxtLVYUBe6Cn33ej9RPG9Yq", 1_700_000_000_000));
        vm.Activity.Add(Sent(Paid, "3vZ67CGoRYkuT76TtpP2VrtTPBfnvG2xj6mUTvvux46qbnpThgQDgm27nC3yQVUZrABFjT4xcBJKCHTnHvK2JN3V", 1_700_000_100_000));
        vm.ActivityFilter = "Transactions";

        vm.BeginEditNoteCommand.Execute(vm.FilteredActivity.Single(r => r.TxId!.StartsWith("5KtP")));
        vm.NoteDraft = "October rent";
        vm.NoteLabelDraft = "Landlord";
        await vm.SaveNoteCommand.ExecuteAsync(null);

        var noted = vm.FilteredActivity.Single(r => r.TxId!.StartsWith("5KtP"));
        var other = vm.FilteredActivity.Single(r => r.TxId!.StartsWith("3vZ6"));
        Assert.Equal("October rent", noted.Note);
        Assert.Null(other.Note);                                    // the note is that transaction's
        Assert.StartsWith("Landlord · ", noted.Detail);             // the name is the address's:
        Assert.StartsWith("Landlord · ", other.Detail);             // every transfer to it carries it
        Assert.False(vm.IsEditingNote);

        // And the Send screen now knows the address by that name.
        vm.SelectedSendAsset = vm.SendableAssetOptions.Single(o => o.Symbol == "SOL");
        vm.SendTo = Paid;
        Assert.True(vm.SendKnownContactShown);
        Assert.Contains("Landlord", vm.SendKnownContact);
    }

    [Fact]
    public async Task Emptying_a_note_and_a_name_removes_them()
    {
        var vm = await UnlockedAsync();
        vm.Activity.Add(Sent(Paid));
        vm.ActivityFilter = "Transactions";
        vm.BeginEditNoteCommand.Execute(vm.FilteredActivity.Single());
        vm.NoteDraft = "to be removed";
        vm.NoteLabelDraft = "Somebody";
        await vm.SaveNoteCommand.ExecuteAsync(null);

        vm.BeginEditNoteCommand.Execute(vm.FilteredActivity.Single());
        Assert.Equal("to be removed", vm.NoteDraft);                // the editor opens with what was saved
        Assert.Equal("Somebody", vm.NoteLabelDraft);
        vm.NoteDraft = "  ";
        vm.NoteLabelDraft = "";
        await vm.SaveNoteCommand.ExecuteAsync(null);

        var row = vm.FilteredActivity.Single();
        Assert.False(row.HasNote);
        Assert.DoesNotContain("Somebody", row.Detail);
    }

    [Fact]
    public async Task Cancelling_keeps_what_was_there()
    {
        var vm = await UnlockedAsync();
        vm.Activity.Add(Sent(Paid));
        vm.ActivityFilter = "Transactions";
        vm.BeginEditNoteCommand.Execute(vm.FilteredActivity.Single());
        vm.NoteDraft = "typed and abandoned";

        vm.CancelEditNoteCommand.Execute(null);

        Assert.False(vm.IsEditingNote);
        Assert.False(vm.FilteredActivity.Single().HasNote);
        Assert.Equal(string.Empty, vm.NoteDraft);
    }
}
