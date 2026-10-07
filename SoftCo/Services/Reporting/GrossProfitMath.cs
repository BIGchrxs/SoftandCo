using SoftCo.Services.Invoicing;

namespace SoftCo.Services.Reporting;

/// <summary>
/// Splitting an order's cost across the projects it serves, and the margin that falls out of it.
///
/// Pure, and written against its tests first. The system could not answer "what did we make on
/// this?" at all before this: <c>OrderProject</c> was a bare join with no amount, so one order
/// serving two projects had its <b>full</b> value counted into <b>each</b> of them. That is why the
/// README has always warned that project balances are not additive, and it is why a gross-profit
/// figure built on the old cost base would have been quietly, confidently wrong.
/// </summary>
public static class GrossProfitMath
{
    /// <summary>Shares are stored at six decimal places, matching numeric(9,6) on the column.</summary>
    public const int ShareDecimals = 6;

    /// <summary>
    /// What counts as summing to one. A share is 6dp, so a handful of them can be a few millionths
    /// out through rounding alone; anything beyond that is a real mis-allocation.
    /// </summary>
    public const decimal ShareTolerance = 0.000001m;

    /// <summary>
    /// An even split that actually sums to 1.
    ///
    /// The difficulty is the recurring case: a third at six places is 0.333333, and three of those
    /// come to 0.999999. The last share absorbs the remainder rather than letting it vanish -
    /// otherwise a slice of every three-project order's cost silently drops out of gross profit,
    /// which would overstate margin on exactly the orders that are hardest to check by hand.
    /// </summary>
    public static IReadOnlyList<decimal> EvenSplit(int count)
    {
        if (count <= 0) return [];

        var each = Math.Round(1m / count, ShareDecimals, MidpointRounding.AwayFromZero);
        var shares = new List<decimal>(count);

        for (var i = 0; i < count - 1; i++) shares.Add(each);
        shares.Add(1m - each * (count - 1));

        return shares;
    }

    /// <summary>
    /// Whether a set of hand-entered shares may be saved.
    ///
    /// Both halves matter. Summing to 1 stops an order's cost being over- or under-counted across
    /// its projects; the per-share bounds stop that being achieved with a negative share, which
    /// would balance arithmetically while crediting cost back to a project that never incurred it.
    /// </summary>
    public static bool SharesAreValid(IReadOnlyList<decimal> shares)
    {
        if (shares.Count == 0) return false;
        if (shares.Any(s => s < 0m || s > 1m)) return false;

        return Math.Abs(shares.Sum() - 1m) <= ShareTolerance;
    }

    /// <summary>This project's slice of an order's cost, to the cent.</summary>
    public static decimal Allocate(decimal amountZar, decimal share) =>
        InvoiceMath.Round(amountZar * share);

    /// <summary>Revenue less cost. A loss is reported as a loss, never clamped to zero.</summary>
    public static decimal GrossProfit(decimal revenue, decimal cost) =>
        InvoiceMath.Round(revenue - cost);

    /// <summary>
    /// Margin as a percentage of revenue, or null when there is no revenue to be a percentage of.
    ///
    /// Null rather than zero or infinity. A project with costs and no invoices yet has no margin -
    /// that is a different statement from "its margin is 0%", and showing 0% would make a project
    /// that has not been billed look like one that broke even.
    /// </summary>
    public static decimal? MarginPercent(decimal revenue, decimal grossProfit) =>
        revenue == 0m ? null : InvoiceMath.Round(grossProfit / revenue * 100m);
}
