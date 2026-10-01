using Umbrella.Wallet.App;
using Umbrella.Wallet.App.ViewModels;
using Xunit;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Activity shows an explorer's amount readably — an Ethereum receipt arrived as
/// "+0.000009698659261008" — while the row keeps every digit for the CSV export.
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public class ActivityAmountDisplayTests
{
    [Fact]
    public void LongAmountsAreShortenedAndWordsPassThrough()
    {
        Fx.SetLanguage("en");

        Assert.Equal("+0.000009698659", ActivityRowViewModel.ShortAmount("+0.000009698659261008"));
        Assert.Equal("-0.00013337", ActivityRowViewModel.ShortAmount("-0.00013337"));
        Assert.Equal("+2.000009", ActivityRowViewModel.ShortAmount("+2.000009"));
        Assert.Equal("+1,234.5", ActivityRowViewModel.ShortAmount("+1234.5"));
        Assert.Equal("unlocked", ActivityRowViewModel.ShortAmount("unlocked"));

        var row = new ActivityRowViewModel("Received", "ETH", "+0.000009698659261008", "0xabc", "now");
        Assert.Equal("+0.000009698659261008", row.Amount);   // the export keeps it all
    }
}
