namespace Umbrella.Wallet.Core.Amounts;

/// <summary>One transfer as it changed a balance: when, and by how much (received positive, sent negative).</summary>
public readonly record struct BalanceMove(long UnixMs, double SignedAmount);

/// <summary>
/// What a wallet held of one coin at each moment of a window, worked back from what it holds now.
///
/// The balance chart used to draw today's holdings at yesterday's prices and say so in small print:
/// a wallet funded an hour ago showed a day of "balance" it never had. With the transfers known, the
/// amount at any earlier moment is today's amount with every later transfer undone — received coins
/// taken back out, sent coins put back.
///
/// Pure arithmetic, no network. It knows only the transfers it is given: before the oldest one, the
/// amount is simply whatever that leaves, and a history with holes gives a line with the same holes.
/// A result below zero means exactly that (a transfer the wallet has not read), and is shown as zero.
/// </summary>
public static class BalanceHistory
{
    /// <summary>
    /// The amount held at each of <paramref name="points"/> evenly spaced moments from
    /// <paramref name="startMs"/> to <paramref name="endMs"/> (the last point is <paramref name="nowAmount"/>).
    /// </summary>
    public static double[] AmountsOver(
        double nowAmount, IEnumerable<BalanceMove> moves, long startMs, long endMs, int points)
    {
        var amounts = new double[Math.Max(points, 0)];
        if (points <= 0) return amounts;

        // Newest first: walking the window backwards, each move is undone once its moment is passed.
        var newestFirst = moves.Where(m => m.UnixMs > 0 && m.SignedAmount != 0)
            .OrderByDescending(m => m.UnixMs).ToArray();
        var next = 0;
        var running = nowAmount;

        for (var i = points - 1; i >= 0; i--)
        {
            var at = points == 1 ? endMs : startMs + (long)((endMs - startMs) * ((double)i / (points - 1)));
            while (next < newestFirst.Length && newestFirst[next].UnixMs > at)
            {
                running -= newestFirst[next].SignedAmount;
                next++;
            }
            amounts[i] = running > 0 ? running : 0;
        }

        return amounts;
    }

    /// <summary>True when at least one of the moves falls inside the window — the line it gives differs
    /// from a flat "what is held now".</summary>
    public static bool AnyWithin(IEnumerable<BalanceMove> moves, long startMs, long endMs) =>
        moves.Any(m => m.UnixMs > startMs && m.UnixMs <= endMs && m.SignedAmount != 0);
}
