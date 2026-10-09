using System.Globalization;

namespace Umbrella.Wallet.Core.Chains;

/// <summary>
/// One coin the wallet can spend, with what a Decred input must repeat about it: the explorer's block
/// height and position-in-block (dcrd compares both with its own record and refuses a mismatch) and the
/// tree it sits in.
/// </summary>
/// <param name="Height">The block it was mined in; 0 while it is unconfirmed.</param>
public sealed record DecredUtxo(string TxId, uint Index, long Atoms, uint Height, uint BlockIndex, byte Tree)
{
    public bool Confirmed => Height > 0;
}

/// <summary>A planned Decred spend: the coins, the payment, the fee and the change. Decided before a key is touched.</summary>
public sealed record DecredSpendPlan(
    IReadOnlyList<DecredUtxo> Inputs,
    long AmountAtoms,
    long FeeAtoms,
    long ChangeAtoms,
    byte[] ToScript,
    byte[] ChangeScript)
{
    public long TotalInputAtoms => Inputs.Sum(u => u.Atoms);

    /// <summary>True when the change was too small to be an output and went to the fee instead.</summary>
    public bool ChangeSweptToFee { get; init; }
}

/// <summary>
/// Choosing the coins for a Decred payment, and every refusal that has to happen before anything is
/// signed. Offline, so each decision is tested.
///
/// The fee depends on how many coins fund the payment, so fee and coin choice are decided together:
/// largest coins first (fewest inputs, smallest fee), priced at <see cref="DecredTransactions.FeePerKb"/>
/// on dcrwallet's upper-bound size estimate.
/// </summary>
public static class DecredSendRules
{
    public const decimal AtomsPerDcr = 100_000_000m;

    /// <summary>Plans a payment of <paramref name="amountAtoms"/> to <paramref name="toScript"/>, or says why it cannot.</summary>
    public static (DecredSpendPlan? Plan, string? Error) Plan(
        IReadOnlyList<DecredUtxo> utxos, long amountAtoms, byte[] toScript, byte[] changeScript)
    {
        if (amountAtoms <= 0) return (null, "Amount must be positive.");

        var dust = DecredTransactions.DustThreshold(toScript.Length);
        if (amountAtoms < dust)
            return (null, $"That amount is below Decred's dust limit ({Dcr(dust)} DCR) and every node would refuse it.");

        // Only confirmed coins: spending an unconfirmed one chains this payment onto a transaction that
        // may still be dropped, and its block height (part of every input) is not known yet.
        var spendable = utxos.Where(u => u.Confirmed && u.Atoms > 0)
            .OrderByDescending(u => u.Atoms)
            .ToList();
        if (spendable.Count == 0)
            return (null, utxos.Count == 0
                ? "This address holds no confirmed Decred to spend."
                : "Every coin at this address is still unconfirmed. Wait for a confirmation and try again.");

        var changeDust = DecredTransactions.DustThreshold(changeScript.Length);
        var chosen = new List<DecredUtxo>();
        long total = 0;

        foreach (var utxo in spendable)
        {
            chosen.Add(utxo);
            total += utxo.Atoms;

            var feeWithChange = DecredTransactions.FeeFor(
                DecredTransactions.EstimateSize(chosen.Count, [toScript.Length, changeScript.Length]));
            if (total >= amountAtoms + feeWithChange)
            {
                var change = total - amountAtoms - feeWithChange;
                if (change >= changeDust)
                    return (new DecredSpendPlan(chosen, amountAtoms, feeWithChange, change, toScript, changeScript), null);
            }

            // No change worth an output — or not even the fee of one — so one output: a smaller
            // transaction, and whatever is left over beyond its fee goes to the fee rather than becoming
            // an output no node would accept. Checked before reaching for another coin.
            var feeNoChange = DecredTransactions.FeeFor(DecredTransactions.EstimateSize(chosen.Count, [toScript.Length]));
            if (total >= amountAtoms + feeNoChange)
                return (new DecredSpendPlan(chosen, amountAtoms, total - amountAtoms, 0, toScript, changeScript)
                    { ChangeSweptToFee = true }, null);
        }

        var fee = DecredTransactions.FeeFor(DecredTransactions.EstimateSize(chosen.Count, [toScript.Length, changeScript.Length]));
        return (null, total >= amountAtoms
            ? $"Not enough DCR to cover the {Dcr(fee)} DCR network fee as well as the amount."
            : $"Not enough DCR: this address holds {Dcr(total)} and the payment needs {Dcr(amountAtoms + fee)}.");
    }

    /// <summary>Everything confirmed, minus the fee for sending it all to one address. 0 when the fee eats it.</summary>
    public static long MaxSendable(IReadOnlyList<DecredUtxo> utxos, int toScriptLength = 25)
    {
        var spendable = utxos.Where(u => u.Confirmed && u.Atoms > 0).ToList();
        if (spendable.Count == 0) return 0;
        var fee = DecredTransactions.FeeFor(DecredTransactions.EstimateSize(spendable.Count, [toScriptLength]));
        var total = spendable.Sum(u => u.Atoms);
        return total > fee ? total - fee : 0;
    }

    /// <summary>DCR to atoms: positive, at most 8 decimal places, nothing rounded away.</summary>
    public static bool TryToAtoms(decimal dcr, out long atoms)
    {
        atoms = 0;
        if (dcr <= 0) return false;
        var scaled = dcr * AtomsPerDcr;
        if (scaled != decimal.Truncate(scaled) || scaled > long.MaxValue) return false;
        atoms = (long)scaled;
        return true;
    }

    public static decimal ToDcr(long atoms) => atoms / AtomsPerDcr;

    // English sentences use a full stop, whatever the machine's locale.
    private static string Dcr(long atoms) => ToDcr(atoms).ToString("0.########", CultureInfo.InvariantCulture);
}
