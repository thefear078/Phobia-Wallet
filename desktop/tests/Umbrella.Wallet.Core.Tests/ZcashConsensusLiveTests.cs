using System.Text.Json;
using System.Text.RegularExpressions;
using Umbrella.Wallet.Core.Chains;
using Xunit;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The two facts about Zcash the wallet cannot check offline, asked of the network itself.
///
/// Marked <c>Category=Live</c> so CI skips it: the suite must never fail because somebody else's
/// server is down. Run it by hand when Zcash announces an upgrade — which is exactly when both of
/// these stop being true, and when a wallet that assumed otherwise starts signing transactions no
/// node will accept.
/// </summary>
[Trait("Category", "Live")]
[Collection(LiveNetworkCollection.Name)]
public class ZcashConsensusLiveTests
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    private const string Zebra = "https://raw.githubusercontent.com/ZcashFoundation/zebra/main/";

    /// <summary>Zebra's mainnet activation heights, by upgrade name (NU6_3, CANOPY …).</summary>
    private static async Task<Dictionary<string, uint>> MainnetHeightsAsync()
    {
        var constants = await Http.GetStringAsync(Zebra + "zebra-chain/src/parameters/constants.rs");
        var start = constants.IndexOf("pub mod mainnet", StringComparison.Ordinal);
        Assert.True(start >= 0, "constants.rs no longer has a mainnet module — the source layout changed.");
        var end = constants.IndexOf("pub mod testnet", start, StringComparison.Ordinal);
        var mainnet = end > start ? constants[start..end] : constants[start..];

        var heights = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(mainnet, @"pub const (\w+): Height = Height\(([\d_]+)\);"))
            heights.TryAdd(m.Groups[1].Value, uint.Parse(m.Groups[2].Value.Replace("_", "")));
        return heights;
    }

    [Fact]
    public async Task Branch_ids_still_match_the_node_zcash_runs_on()
    {
        // zcashd — read here until 2026-10-14 — is deprecated and stopped listing upgrades at NU6.2, so
        // this check passed while the wallet was a whole upgrade (NU6.3) behind. Zebra is the node.
        var upgrades = await Http.GetStringAsync(Zebra + "zebra-chain/src/parameters/network_upgrade.rs");
        var branches = Regex.Matches(upgrades, @"\((\w+), ConsensusBranchId\(0x([0-9a-fA-F]+)\)\)")
            .Select(m => (Name: m.Groups[1].Value, Id: Convert.ToUInt32(m.Groups[2].Value, 16)))
            .Where(b => b.Name != "ZFuture")
            .ToList();
        var heights = await MainnetHeightsAsync();

        Assert.NotEmpty(branches);
        var scheduled = 0;
        foreach (var (name, id) in branches)
        {
            if (!heights.TryGetValue(name, out var height)) continue;   // not scheduled on mainnet (NU7)
            Assert.True(id == ZcashTransactions.ConsensusBranchId(height), $"{name} at {height}: Zebra says 0x{id:x8}, the wallet 0x{ZcashTransactions.ConsensusBranchId(height):x8}");
            Assert.NotEqual(id, ZcashTransactions.ConsensusBranchId(height - 1));
            scheduled++;
        }

        // Overwinter through the newest scheduled one; fewer means the names no longer line up.
        Assert.True(scheduled >= 10, $"only {scheduled} upgrades matched to a mainnet height — the source layout changed.");
    }

    [Fact]
    public async Task Version_4_is_still_accepted_under_the_newest_mainnet_upgrade()
    {
        // The wallet builds v4. Wallets have moved to v5 and v6, so counting versions in recent blocks
        // says nothing (on 2026-10-14 every one sampled was v6). The node's own rule is asked instead:
        // the upgrades its v4 check lets through, against the newest one scheduled on mainnet.
        var heights = await MainnetHeightsAsync();
        var newest = heights.Where(h => h.Key.StartsWith("NU", StringComparison.Ordinal))
            .OrderByDescending(h => h.Value).First().Key;   // "NU6_3"
        var variant = "NetworkUpgrade::Nu" + newest[2..];   // "NetworkUpgrade::Nu6_3"

        var verifier = await Http.GetStringAsync(Zebra + "zebra-consensus/src/transaction.rs");
        var start = verifier.IndexOf("fn verify_v4_transaction_network_upgrade", StringComparison.Ordinal);
        Assert.True(start >= 0, "the v4 rule is no longer where it was — the source layout changed.");
        var accepted = verifier[start..verifier.IndexOf("=> Ok(())", start, StringComparison.Ordinal)];

        Assert.Matches(Regex.Escape(variant) + @"(?!\w)", accepted);   // Nu6_3 itself, not Nu6 or Nu6_1
    }

    [Fact]
    public async Task A_node_decodes_the_transaction_and_stops_only_at_the_missing_coin()
    {
        // The signature hash is pinned to Zcash's vectors; the WIRE FORMAT is not, because those vectors
        // carry shielded parts this wallet never builds. So the network is asked: a transaction built
        // exactly as a send would be, signed by a key made up for this test, spending a coin that does
        // not exist. A node that cannot parse it answers "decode failed"; one that parses it and runs
        // the context-free checks (version group, expiry, amounts) gets as far as looking the coin up,
        // and stops there. Nothing can be spent: the coin is invented and the key holds nothing.
        using var key = new NBitcoin.Key();
        var from = ZcashAddress.EncodePublicKeyHash(key.PubKey.Hash.ToBytes());
        var fromScript = ZcashAddress.TryDecode(from).Address!.ScriptPubKey;

        using var stats = await Json("https://api.blockchair.com/zcash/stats");
        var next = (uint)stats.RootElement.GetProperty("data").GetProperty("best_block_height").GetInt64() + 1;
        var expiry = ZcashTransactions.ExpiryHeight(next) ?? throw new InvalidOperationException("Upgrade imminent.");

        var invented = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var quote = new Umbrella.Wallet.Infrastructure.Network.ZecSendQuote(
            From: from, To: "t3cFfPt1Bcvgez9ZbMBFWeZsskxTkPzGCow", Amount: 0.001m,
            AmountZat: 100_000, FeeZat: 10_000, FeeZec: 0.0001m, ChangeZat: 90_000,
            ExpiryHeight: expiry, BranchId: ZcashTransactions.ConsensusBranchId(next),
            Inputs: [new ZcashUtxo(invented, 0, 200_000, next - 10)],
            FromScript: fromScript,
            ToScript: ZcashAddress.TryDecode("t3cFfPt1Bcvgez9ZbMBFWeZsskxTkPzGCow").Address!.ScriptPubKey,
            ChangeSweptToFee: false);
        var (raw, _) = Umbrella.Wallet.Infrastructure.Network.ZcashTransactionSender.Build(quote, key);

        using var content = new FormUrlEncodedContent([new("data", Convert.ToHexString(raw).ToLowerInvariant())]);
        using var res = await Http.PostAsync("https://api.blockchair.com/zcash/push/transaction", content);
        var body = await res.Content.ReadAsStringAsync();

        Assert.False(res.IsSuccessStatusCode, body);
        Assert.DoesNotContain("decode", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("missing", body, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<JsonDocument> Json(string url) =>
        JsonDocument.Parse(await Http.GetStringAsync(url));
}
