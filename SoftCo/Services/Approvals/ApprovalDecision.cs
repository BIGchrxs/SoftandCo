using SoftCo.Models;

namespace SoftCo.Services.Approvals;

/// <summary>
/// The bookkeeping every decision shares: what was decided, by whom, when and why.
///
/// Extracted so purchase orders and payment releases cannot drift apart on it. The two differ in
/// what they do to their subject afterwards - one sets a status on an order, the other on a payment
/// request - but recording the decision itself is identical, and an approval that forgot to stamp
/// who made it would be worthless as evidence.
/// </summary>
public static class ApprovalDecision
{
    public static void Record(Approval approval, ApprovalStatus status, Actor actor,
                              string? reason, DateTime utcNow)
    {
        approval.Status = status;
        approval.DecidedAt = utcNow;
        approval.DecidedById = actor.Id;
        approval.DecidedByName = actor.Name;
        approval.DecisionReason = Trim(reason);
    }

    /// <summary>Keeps free text inside the column it is stored in.</summary>
    public static string? Trim(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var trimmed = value.Trim();
        return trimmed.Length > 1000 ? trimmed[..1000] : trimmed;
    }
}
