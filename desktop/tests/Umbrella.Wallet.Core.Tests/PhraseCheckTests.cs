using Umbrella.Wallet.App;
using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The paper check (roadmap P1.8): the phrase the user wrote down is compared with the wallet's own and
/// the answer is "it restores this wallet" or which positions differ — the phrase itself is never shown.
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class PhraseCheckTests : IDisposable
{
    private const string Phrase = "abandon ability able about above absent absorb abstract absurd abuse access accident";
    private const string Password = "umbrella-test-vault-2026";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"umbrella-phrasecheck-{Guid.NewGuid():N}");

    public void Dispose()
    {
        TestDataIsolation.RestoreBaselineSettings();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Theory]
    [InlineData("abandon ability able about above absent absorb abstract absurd abuse access accident")]
    [InlineData("ABANDON Ability able about above absent absorb abstract absurd abuse access accident")]
    [InlineData("1. abandon 2. ability 3. able 4. about\n5. above 6. absent 7. absorb 8. abstract\n9. absurd 10. abuse 11. access 12. accident")]
    [InlineData("1)abandon 2)ability 3)able 4)about 5)above 6)absent 7)absorb 8)abstract 9)absurd 10)abuse 11)access 12)accident")]
    [InlineData("abandon, ability, able, about, above, absent, absorb, abstract, absurd, abuse, access, accident")]
    [InlineData("  abandon   ability\table about above absent absorb abstract absurd abuse access accident  ")]
    public void The_paper_is_read_the_way_people_copy_a_phrase(string typed)
    {
        var result = PhraseCheck.Compare(Phrase, typed);

        Assert.True(result.Matches);
        Assert.Empty(result.Mismatches);
        Assert.Equal(12, result.StoredCount);
    }

    [Fact]
    public void A_wrong_word_is_named_by_its_position()
    {
        var typed = Phrase.Replace("absorb", "absurd", StringComparison.Ordinal);

        var result = PhraseCheck.Compare(Phrase, typed);

        Assert.False(result.Matches);
        Assert.Equal(new[] { 7 }, result.Mismatches);
    }

    [Fact]
    public void Two_swapped_words_are_both_named()
    {
        var typed = "ability abandon able about above absent absorb abstract absurd abuse access accident";

        Assert.Equal(new[] { 1, 2 }, PhraseCheck.Compare(Phrase, typed).Mismatches);
    }

    [Fact]
    public void A_missing_word_is_a_count_not_a_list_of_every_position_after_it()
    {
        var typed = "abandon ability able about above absent absorb abstract absurd abuse access";

        var result = PhraseCheck.Compare(Phrase, typed);

        Assert.False(result.Matches);
        Assert.Equal(11, result.TypedCount);
        Assert.Equal(12, result.StoredCount);
        Assert.Empty(result.Mismatches);
    }

    [Fact]
    public void Nothing_typed_is_no_match()
    {
        Assert.False(PhraseCheck.Compare(Phrase, "   ").Matches);
        Assert.Empty(PhraseCheck.Words(null));
    }

    // ---- in the wallet ---------------------------------------------------------------------------------

    private async Task<(MainViewModel Vm, string Phrase)> NewWalletAsync()
    {
        TestDataIsolation.RestoreBaselineSettings();
        var vm = new MainViewModel(new EncryptedFileSeedVault(Path.Combine(_directory, "vault.json")));
        vm.Password = Password;
        vm.ConfirmPassword = Password;
        await vm.CreateWalletCommand.ExecuteAsync(null);
        var phrase = vm.RecoveryPhrase;
        vm.ConfirmPhraseBackupCommand.Execute(null);
        await BackgroundRefresh.SettleAsync(vm);
        return (vm, phrase);
    }

    [Fact]
    public async Task The_right_paper_is_confirmed_and_nothing_typed_is_kept()
    {
        var (vm, phrase) = await NewWalletAsync();
        Assert.False(string.IsNullOrWhiteSpace(phrase));

        vm.SettingsPassword = Password;
        vm.PhraseCheckInput = phrase.ToUpperInvariant();
        await vm.CheckWrittenPhraseCommand.ExecuteAsync(null);

        Assert.True(vm.PhraseCheckOk);
        Assert.Equal(string.Empty, vm.PhraseCheckInput);
        Assert.Equal(string.Empty, vm.SettingsPassword);
        Assert.DoesNotContain(phrase.Split(' ')[0], vm.PhraseCheckResult, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_paper_with_a_wrong_word_says_which_one()
    {
        var (vm, phrase) = await NewWalletAsync();
        var words = phrase.Split(' ');
        words[4] = words[4] == "zoo" ? "zone" : "zoo";

        vm.SettingsPassword = Password;
        vm.PhraseCheckInput = string.Join(' ', words);
        await vm.CheckWrittenPhraseCommand.ExecuteAsync(null);

        Assert.False(vm.PhraseCheckOk);
        Assert.Contains("5", vm.PhraseCheckResult, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_wrong_password_checks_nothing()
    {
        var (vm, phrase) = await NewWalletAsync();

        vm.SettingsPassword = "not-the-vault-password";
        vm.PhraseCheckInput = phrase;
        await vm.CheckWrittenPhraseCommand.ExecuteAsync(null);

        Assert.False(vm.PhraseCheckOk);
        Assert.Equal(Loc.Instance["backup.check.wrongPassword"], vm.PhraseCheckResult);
    }

    [Fact]
    public async Task Locking_forgets_the_check()
    {
        var (vm, phrase) = await NewWalletAsync();
        vm.PhraseCheckInput = phrase;

        vm.LockVault();

        Assert.Equal(string.Empty, vm.PhraseCheckInput);
    }
}
