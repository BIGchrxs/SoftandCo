using SoftCo.Models;

namespace SoftCo.Services.Reporting;

/// <summary>
/// Writes allocation shares onto an order's project links. Shared by the international and local
/// order forms, which both render _ProjectAllocation and so both offer a split.
/// </summary>
public static class OrderAllocation
{
    /// <summary>
    /// One project takes the whole cost. Several with nothing entered get an even split; several
    /// with shares entered must come to 100%, because shares summing to anything else over- or
    /// under-states the cost base of every project the order touches and nothing downstream
    /// catches it.
    /// </summary>
    /// <param name="links">The order's project links, updated in place.</param>
    /// <param name="enteredPercents">Shares as posted, keyed by project id, as percentages.</param>
    /// <param name="error">Why the shares were refused, when the result is false.</param>
    public static bool Apply(
        ICollection<OrderProject> links,
        IReadOnlyDictionary<int, decimal?> enteredPercents,
        out string? error)
    {
        error = null;

        // Ordered by project id so the remainder lands on the same link every time.
        var ordered = links.OrderBy(op => op.ProjectId).ToList();

        if (ordered.Count == 0) return true;

        if (ordered.Count == 1)
        {
            ordered[0].AllocationShare = 1m;
            return true;
        }

        var entered = ordered
            .Select(op => enteredPercents.TryGetValue(op.ProjectId, out var pct) && pct is decimal p
                ? p / 100m
                : (decimal?)null)
            .ToList();

        // A blank box is "not stated", not zero, so fall back to even rather than refusing.
        if (entered.Any(e => e is null))
        {
            var even = GrossProfitMath.EvenSplit(ordered.Count);
            for (var i = 0; i < ordered.Count; i++) ordered[i].AllocationShare = even[i];
            return true;
        }

        var shares = entered
            .Select(e => Math.Round(e!.Value, GrossProfitMath.ShareDecimals,
                                    MidpointRounding.AwayFromZero))
            .ToList();

        if (!GrossProfitMath.SharesAreValid(shares))
        {
            error = $"The project shares add up to {shares.Sum() * 100m:0.##}%. They have to come to 100%.";
            return false;
        }

        for (var i = 0; i < ordered.Count; i++) ordered[i].AllocationShare = shares[i];
        return true;
    }
}
