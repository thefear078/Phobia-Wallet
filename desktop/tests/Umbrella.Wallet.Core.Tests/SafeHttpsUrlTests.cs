using Umbrella.Wallet.App.ViewModels;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Links reach the system's browser only through <see cref="MainViewModel.SafeHttpsUrl"/>. Links come from
/// explorers and swap services too, so anything that is not a plain https:// address is dropped, not opened.
/// </summary>
public sealed class SafeHttpsUrlTests
{
    [Theory]
    [InlineData("https://mempool.space/tx/abc", "https://mempool.space/tx/abc")]
    [InlineData("  https://github.com/thefear078/Phobia-Wallet  ", "https://github.com/thefear078/Phobia-Wallet")]
    public void A_plain_https_address_is_opened(string input, string expected) =>
        Assert.Equal(expected, MainViewModel.SafeHttpsUrl(input)?.AbsoluteUri);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://mempool.space/tx/abc")]              // not encrypted
    [InlineData("file:///C:/Windows/System32/calc.exe")]     // a program, not a page
    [InlineData("C:\\Windows\\System32\\calc.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https:calc.exe")]                           // prefix-matching would have let this through
    [InlineData("https://user:pass@mempool.space/")]         // credentials in the link
    [InlineData("https://mempool.space/\n\"--x")]            // control characters
    [InlineData("mempool.space/tx/abc")]                     // not absolute
    public void Anything_else_is_dropped(string? input) =>
        Assert.Null(MainViewModel.SafeHttpsUrl(input));
}
