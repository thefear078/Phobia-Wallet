using Umbrella.Wallet.Core.Amounts;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The balance chart drew today's holdings at yesterday's prices. A wallet that had received its first
/// coins a few hours earlier showed a whole day of "balance" before that, and a cliff at the right-hand
/// edge. What was actually held at each moment is today's amount with every later transfer undone —
/// this is that arithmetic.
/// </summary>
public sealed class BalanceHistoryTests
{
    private const long Hour = 3_600_000;

    [Fact]
    public void Without_transfers_the_amount_is_the_same_all_the_way_back()
    {
        var amounts = BalanceHistory.AmountsOver(2.5, [], startMs: 0, endMs: 24 * Hour, points: 5);

        Assert.Equal(new[] { 2.5, 2.5, 2.5, 2.5, 2.5 }, amounts);
    }

    [Fact]
    public void A_wallet_funded_inside_the_window_held_nothing_before_that()
    {
        // Nothing, then +0.002 at hour 20 of 24, then two small sends.
        var moves = new[]
        {
            new BalanceMove(20 * Hour, 0.002),
            new BalanceMove(22 * Hour, -0.0001),
            new BalanceMove(23 * Hour, -0.0002),
        };

        var amounts = BalanceHistory.AmountsOver(0.0017, moves, startMs: 0, endMs: 24 * Hour, points: 25);

        Assert.Equal(0, amounts[0], 12);                    // midnight: the wallet was empty
        Assert.Equal(0, amounts[19], 12);                   // an hour before the money came: still empty
        Assert.Equal(0.002, amounts[20], 10);               // from the moment it arrived
        Assert.Equal(0.002, amounts[21], 10);
        Assert.Equal(0.0019, amounts[22], 10);              // after the first send
        Assert.Equal(0.0017, amounts[24], 10);              // now
    }

    [Fact]
    public void A_coin_sent_away_an_hour_ago_was_still_held_this_morning()
    {
        var moves = new[] { new BalanceMove(23 * Hour, -1.0) };

        var amounts = BalanceHistory.AmountsOver(0, moves, startMs: 0, endMs: 24 * Hour, points: 25);

        Assert.Equal(1.0, amounts[0]);
        Assert.Equal(1.0, amounts[22]);
        Assert.Equal(0.0, amounts[23]);                     // the send's own moment: gone
        Assert.Equal(0.0, amounts[24]);
    }

    [Fact]
    public void Transfers_before_the_window_do_not_bend_the_line()
    {
        var moves = new[] { new BalanceMove(-5 * Hour, 3.0) };

        var amounts = BalanceHistory.AmountsOver(3.0, moves, startMs: 0, endMs: 24 * Hour, points: 4);

        Assert.All(amounts, a => Assert.Equal(3.0, a));
        Assert.False(BalanceHistory.AnyWithin(moves, 0, 24 * Hour));
    }

    [Fact]
    public void A_history_with_a_hole_never_draws_a_negative_balance()
    {
        // More was received inside the window than is held now, and the send that explains it has not
        // been read: undoing the receipt would go below zero. Zero is the honest floor.
        var moves = new[] { new BalanceMove(12 * Hour, 5.0) };

        var amounts = BalanceHistory.AmountsOver(1.0, moves, startMs: 0, endMs: 24 * Hour, points: 25);

        Assert.All(amounts, a => Assert.True(a >= 0));
        Assert.Equal(1.0, amounts[24]);
    }

    [Fact]
    public void The_order_the_transfers_arrive_in_does_not_matter()
    {
        var moves = new[] { new BalanceMove(6 * Hour, 1.0), new BalanceMove(18 * Hour, 2.0), new BalanceMove(12 * Hour, -0.5) };

        var forwards = BalanceHistory.AmountsOver(2.5, moves, 0, 24 * Hour, 25);
        var backwards = BalanceHistory.AmountsOver(2.5, moves.Reverse(), 0, 24 * Hour, 25);

        Assert.Equal(forwards, backwards);
        Assert.Equal(0.0, forwards[0], 12);
        Assert.Equal(1.0, forwards[6], 12);
        Assert.Equal(0.5, forwards[12], 12);
        Assert.Equal(2.5, forwards[18], 12);
    }

    [Fact]
    public void Rows_without_a_time_or_an_amount_are_ignored()
    {
        var moves = new[] { new BalanceMove(0, 9.0), new BalanceMove(5 * Hour, 0) };

        var amounts = BalanceHistory.AmountsOver(1.0, moves, startMs: -Hour, endMs: 24 * Hour, points: 3);

        Assert.All(amounts, a => Assert.Equal(1.0, a));
    }
}
