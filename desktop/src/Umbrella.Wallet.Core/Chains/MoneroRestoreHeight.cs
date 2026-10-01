using System.Globalization;

namespace Umbrella.Wallet.Core.Chains;

/// <summary>
/// Where to start scanning for an imported Monero seed. A 25-word seed does not record when the wallet was
/// made, so without a hint the scan starts at the chain's first block — correct, but hours of work against
/// a remote node. The user may give either a block height or the date the wallet was created (any day
/// before it is fine), and the date is turned into a height a week early, so a rough date never misses a
/// payment.
/// </summary>
public static class MoneroRestoreHeight
{
    /// <summary>Scanned a week before the date asked for, so "around March" still finds a March 1 payment.</summary>
    public const ulong SafetyBlocks = 7 * 720;

    /// <summary>
    /// (height, unix time) every 100,000 blocks, read from two public Monero nodes (block 1 stands in for the
    /// genesis block, whose timestamp is 0). Monero's block time was 1 minute until block 1,009,827
    /// (2016-03-23) and 2 minutes since; interpolating between neighbours covers both.
    /// </summary>
    private static readonly (ulong Height, long Time)[] Anchors =
    [
        (1, 1397818193),
        (100_000, 1403639801), (200_000, 1409644412), (300_000, 1415690591), (400_000, 1421749732),
        (500_000, 1427785048), (600_000, 1433825715), (700_000, 1439872751), (800_000, 1445938467),
        (900_000, 1452044620), (1_000_000, 1458145453), (1_100_000, 1469573151), (1_200_000, 1481575804),
        (1_300_000, 1493568547), (1_400_000, 1505525706), (1_500_000, 1517489351), (1_600_000, 1529603343),
        (1_700_000, 1541612717), (1_800_000, 1553698458), (1_900_000, 1565709489), (2_000_000, 1577680194),
        (2_100_000, 1589683808), (2_200_000, 1601703776), (2_300_000, 1613707976), (2_400_000, 1625715884),
        (2_500_000, 1637720692), (2_600_000, 1649731449), (2_700_000, 1661752700), (2_800_000, 1673769923),
        (2_900_000, 1685787011), (3_000_000, 1697813342), (3_100_000, 1709832655), (3_200_000, 1721843329),
        (3_300_000, 1733848630), (3_400_000, 1745849558), (3_500_000, 1757868614), (3_600_000, 1769885820),
        (3_700_000, 1781905872),
    ];

    /// <summary>Two minutes a block, past the last anchor.</summary>
    private const long SecondsPerBlock = 120;

    /// <summary>The block anchors are exposed for the live test that re-reads them from a node.</summary>
    public static IReadOnlyList<(ulong Height, long Time)> KnownBlocks => Anchors;

    /// <summary>
    /// Reads what the user typed: empty (scan everything — returns 0), a block height ("3200000",
    /// "3 200 000", "3,200,000") or a date ("2024-05-01"). False when it is neither, or a date in the future.
    /// </summary>
    public static bool TryParse(string? input, DateTimeOffset now, out ulong height)
    {
        height = 0;
        var text = (input ?? string.Empty).Trim();
        if (text.Length == 0) return true;

        if (DateTime.TryParseExact(text, ["yyyy-MM-dd", "yyyy-M-d", "yyyy/MM/dd", "yyyy.MM.dd", "dd.MM.yyyy"],
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var date))
        {
            var when = new DateTimeOffset(date, TimeSpan.Zero);
            if (when > now) return false;
            height = ForDate(when);
            return true;
        }

        var digits = new string(text.Where(c => !char.IsWhiteSpace(c) && c is not ',' and not '_' and not '\'').ToArray());
        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit)
            || !ulong.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out height))
        {
            height = 0;
            return false;
        }

        // A height past the chain's tip would make the wallet wait for blocks that do not exist yet and skip
        // everything already received — the estimate plus a month of slack is the most that can be meant.
        if (height > ForDate(now) + SafetyBlocks + 30 * 720)
        {
            height = 0;
            return false;
        }
        return true;
    }

    /// <summary>A height safely before the given date: the interpolated block, minus a week.</summary>
    public static ulong ForDate(DateTimeOffset date)
    {
        var t = date.ToUnixTimeSeconds();
        if (t <= Anchors[0].Time) return 0;

        ulong estimate;
        var last = Anchors[^1];
        if (t >= last.Time)
        {
            estimate = last.Height + (ulong)((t - last.Time) / SecondsPerBlock);
        }
        else
        {
            var i = Array.FindLastIndex(Anchors, a => a.Time <= t);
            var (h0, t0) = Anchors[i];
            var (h1, t1) = Anchors[i + 1];
            estimate = h0 + (ulong)((double)(t - t0) / (t1 - t0) * (h1 - h0));
        }

        return estimate > SafetyBlocks ? estimate - SafetyBlocks : 0;
    }
}
