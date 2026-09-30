using System.Text.Json;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Where the Monero wallet starts scanning. It used to be "the last ~30 days" — which meant a restore on a
/// new PC, or after a wipe, never found Monero received more than a month earlier. The account derived from
/// the recovery phrase cannot have received anything before Umbrella's Monero derivation first shipped
/// (2026-07-23), so every scan starts from a block safely before that date, and a wallet created by an older
/// build is restored again, once, over the full range.
/// </summary>
public sealed class MoneroRestoreHeightTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"umbrella-xmr-{Guid.NewGuid():N}");

    public MoneroRestoreHeightTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void A_wallet_created_by_an_older_build_is_restored_again()
    {
        var wallet = Path.Combine(_dir, "umbrella-4abcdef01234");
        File.WriteAllText(wallet, "cache");
        File.WriteAllText(wallet + ".keys", "keys");

        Assert.True(MoneroRpcService.NeedsFullRestore(wallet));   // no record of where it scans from
    }

    [Fact]
    public void A_wallet_this_build_created_is_left_alone()
    {
        var wallet = Path.Combine(_dir, "umbrella-4abcdef01234");
        File.WriteAllText(wallet, "cache");
        File.WriteAllText(wallet + MoneroRpcService.ScanFromSuffix, MoneroRpcService.DerivedAccountFloorHeight.ToString());

        Assert.False(MoneroRpcService.NeedsFullRestore(wallet));
    }

    [Fact]
    public void No_wallet_yet_means_nothing_to_restore_again()
    {
        Assert.False(MoneroRpcService.NeedsFullRestore(Path.Combine(_dir, "umbrella-none")));
    }

    [Fact]
    public void The_floor_is_before_the_derivation_ever_existed()
    {
        // 2026-07-23 is the first commit carrying "umbrella-monero-v1". Monero makes a block every ~2
        // minutes (720 a day); the floor must sit well before that date, and the live test below checks
        // the block's own timestamp.
        Assert.True(MoneroRpcService.DerivedAccountFloorHeight <= 3_700_000UL);
    }

    [Fact]
    public void Every_rpc_call_states_its_length()
    {
        // epee (monerod and monero-wallet-rpc) answers a chunked body with "Invalid Request". A body without
        // a known length goes out chunked; this one must not — that was every Monero call until 2026-09-30.
        using var body = MoneroRpcService.RequestBody("get_balance", new { account_index = 0 });
        Assert.NotNull(body.Headers.ContentLength);
        Assert.True(body.Headers.ContentLength > 0);

        var json = body.ReadAsStringAsync().GetAwaiter().GetResult();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("2.0", doc.RootElement.GetProperty("jsonrpc").GetString());
        Assert.Equal("get_balance", doc.RootElement.GetProperty("method").GetString());
        Assert.Equal(0, doc.RootElement.GetProperty("params").GetProperty("account_index").GetInt32());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}

/// <summary>The floor block's date, asked of public Monero nodes. <c>Category=Live</c>: run by hand.</summary>
[Trait("Category", "Live")]
public sealed class MoneroRestoreHeightLiveTests
{
    [Theory]
    [InlineData("https://xmr-node.cakewallet.com:18081/json_rpc")]
    [InlineData("https://node.sethforprivacy.com/json_rpc")]
    public async Task The_floor_block_was_mined_before_the_derivation_shipped(string node)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // The exact body the wallet sends to monero-wallet-rpc — the same HTTP server code as monerod, so a
        // node reading it is the proof that the wallet's own calls are read too.
        using var request = MoneroRpcService.RequestBody(
            "get_block_header_by_height", new { height = MoneroRpcService.DerivedAccountFloorHeight });
        using var res = await http.PostAsync(node, request);
        var body = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("result", out _), body);
        var seconds = doc.RootElement.GetProperty("result").GetProperty("block_header").GetProperty("timestamp").GetInt64();

        Assert.True(DateTimeOffset.FromUnixTimeSeconds(seconds) < new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero));
    }
}
