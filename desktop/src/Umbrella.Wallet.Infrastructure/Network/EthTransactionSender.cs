using System.Globalization;
using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json;
using Nethereum.Signer;

namespace Umbrella.Wallet.Infrastructure.Network;

public sealed record EthSendQuote(
    string From,
    string To,
    decimal AmountEth,
    BigInteger AmountWei,
    BigInteger Nonce,
    BigInteger GasPriceWei,
    decimal MaxFeeEth,
    string Rpc);

public sealed record EthSendResult(bool Ok, string? TxHash, string? Error);

/// <summary>
/// Real Ethereum mainnet send: nonce + gas price come from public RPCs, the transaction is
/// signed LOCALLY (EIP-155, chainId 1) with the key derived from the vault mnemonic, and only
/// the signed raw bytes are broadcast. The private key never leaves the process.
/// The signer is pinned to the official EIP-155 test vector in the test suite.
/// </summary>
public sealed class EthTransactionSender
{
    private const long ChainId = 1;
    private const long TransferGasLimit = 21_000;

    private static readonly string[] Rpcs =
    [
        "https://cloudflare-eth.com",
        "https://rpc.ankr.com/eth",
        "https://eth.drpc.org",
    ];

    private static HttpClient Http => PublicHttp.Shared;

    /// <summary>
    /// Prepares a send: validates inputs, fetches balance / nonce / gas price, and returns a
    /// quote for explicit user confirmation. Nothing is signed here.
    /// </summary>
    public async Task<(EthSendQuote? Quote, string? Error)> PrepareAsync(
        string fromAddress,
        string toAddress,
        decimal amountEth,
        CancellationToken ct = default)
    {
        if (!IsHexAddress(toAddress))
        {
            return (null, "Destination must be a 0x… Ethereum address (42 characters).");
        }

        if (amountEth <= 0)
        {
            return (null, "Amount must be positive.");
        }

        var amountWei = new BigInteger(amountEth * 1_000_000_000_000_000_000m);

        foreach (var rpc in Rpcs)
        {
            try
            {
                var balanceHex = await CallAsync(rpc, "eth_getBalance", new object[] { fromAddress, "latest" }, ct);
                var nonceHex = await CallAsync(rpc, "eth_getTransactionCount", new object[] { fromAddress, "pending" }, ct);
                var gasHex = await CallAsync(rpc, "eth_gasPrice", Array.Empty<object>(), ct);
                if (balanceHex is null || nonceHex is null || gasHex is null) continue;

                var balance = FromHex(balanceHex);
                var nonce = FromHex(nonceHex);
                var gasPrice = FromHex(gasHex);
                // 5% headroom: enough that a small gas-price move between quote and broadcast
                // doesn't strand the transaction, without overpaying the way a 20% pad did.
                var paddedGasPrice = gasPrice * 105 / 100;
                var maxFeeWei = paddedGasPrice * TransferGasLimit;

                if (balance < amountWei + maxFeeWei)
                {
                    var haveEth = (decimal)balance / 1_000_000_000_000_000_000m;
                    return (null,
                        $"Insufficient funds: balance {haveEth:0.######} ETH, " +
                        $"need {amountEth:0.######} ETH + ~{(decimal)maxFeeWei / 1_000_000_000_000_000_000m:0.######} ETH fee.");
                }

                return (new EthSendQuote(
                    fromAddress, toAddress, amountEth, amountWei, nonce, paddedGasPrice,
                    (decimal)maxFeeWei / 1_000_000_000_000_000_000m, rpc), null);
            }
            catch
            {
                // try next RPC
            }
        }

        return (null, "All public Ethereum RPCs are unreachable — check your connection (or Tor).");
    }

    /// <summary>Signs the quoted transfer with the given private key and broadcasts it.</summary>
    public async Task<EthSendResult> SignAndBroadcastAsync(
        EthSendQuote quote,
        byte[] privateKey,
        CancellationToken ct = default)
    {
        string signedHex;
        try
        {
            signedHex = SignTransfer(
                privateKey, quote.To, quote.AmountWei, quote.Nonce, quote.GasPriceWei, TransferGasLimit);
        }
        catch (Exception ex)
        {
            return new EthSendResult(false, null, $"Signing failed: {ex.Message}");
        }

        foreach (var rpc in Rpcs)
        {
            try
            {
                using var res = await Http.PostAsJsonAsync(rpc, new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "eth_sendRawTransaction",
                    @params = new object[] { "0x" + signedHex },
                }, ct);
                if (!res.IsSuccessStatusCode) continue;

                using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                if (doc.RootElement.TryGetProperty("result", out var result) &&
                    result.ValueKind == JsonValueKind.String)
                {
                    return new EthSendResult(true, result.GetString(), null);
                }

                if (doc.RootElement.TryGetProperty("error", out var error))
                {
                    var message = error.TryGetProperty("message", out var m) ? m.GetString() : "RPC rejected the transaction";
                    // A node-level rejection (bad nonce, underpriced, insufficient funds) is
                    // final — retrying another RPC with the same bytes will fail the same way.
                    return new EthSendResult(false, null, message);
                }
            }
            catch
            {
                // network issue — try next RPC
            }
        }

        return new EthSendResult(false, null, "Broadcast failed: no RPC accepted the transaction.");
    }

    /// <summary>
    /// EIP-155 legacy transfer signature (chainId 1). Public so the test suite can pin it to the
    /// official EIP-155 example transaction byte-for-byte.
    /// </summary>
    public static string SignTransfer(
        byte[] privateKey,
        string to,
        BigInteger amountWei,
        BigInteger nonce,
        BigInteger gasPriceWei,
        BigInteger gasLimit)
    {
        var signer = new LegacyTransactionSigner();
        return signer.SignTransaction(privateKey, ChainId, to, amountWei, nonce, gasPriceWei, gasLimit);
    }

    private static async Task<string?> CallAsync(string rpc, string method, object[] args, CancellationToken ct)
    {
        using var res = await Http.PostAsJsonAsync(rpc, new
        {
            jsonrpc = "2.0",
            id = 1,
            method,
            @params = args,
        }, ct);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement.TryGetProperty("result", out var result) ? result.GetString() : null;
    }

    private static BigInteger FromHex(string hex) =>
        BigInteger.Parse("0" + hex.TrimStart('0', 'x', 'X').PadLeft(1, '0'), NumberStyles.HexNumber);

    private static bool IsHexAddress(string value) =>
        value is { Length: 42 } &&
        value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
        value.Skip(2).All(Uri.IsHexDigit);
}
