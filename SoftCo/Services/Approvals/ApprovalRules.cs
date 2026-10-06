using SoftCo.Models;

namespace SoftCo.Services.Approvals;

/// <summary>
/// The outcome of checking a rule: allowed, or refused with something worth showing a person.
/// </summary>
public sealed record RuleResult(bool Ok, string? Message)
{
    public static readonly RuleResult Allowed = new(true, null);
    public static RuleResult Refuse(string message) => new(false, message);
}

/// <summary>
/// Who may do what to a purchase-order approval, and when.
///
/// Pure: no database, no HTTP, no clock. Every rule here is one a reviewer should be able to read
/// and an auditor should be able to see tested, which is hard to do when the same logic is spread
/// through a controller. <c>PurchaseOrderApprovalService</c> is the only caller and does the I/O.
/// </summary>
public static class ApprovalRules
{
    /// <summary>
    /// A rejection has to say why. Short enough that a considered "price too high, renegotiate"
    /// passes, long enough that "no" does not - somebody has to act on this, and a one-word
    /// rejection turns into a conversation that happens off the record.
    /// </summary>
    public const int MinimumRejectionReasonLength = 10;

    /// <summary>
    /// Whether an order is complete enough to ask anyone to approve it.
    ///
    /// The Financial Director is being asked to commit money to a supplier for a project. An order
    /// missing any of those three is not a decision they can make, and sending it anyway wastes the
    /// one person whose time this system is meant to protect.
    /// </summary>
    public static RuleResult CanSubmit(PoApprovalStatus status, bool hasSupplier,
                                       decimal valueZar, bool hasProject)
    {
        if (status == PoApprovalStatus.PendingApproval)
            return RuleResult.Refuse("This order is already waiting for approval.");

        if (status == PoApprovalStatus.Issued)
            return RuleResult.Refuse("This purchase order has already been issued.");

        if (!hasSupplier)
            return RuleResult.Refuse("Choose the supplier before sending this for approval.");

        if (valueZar <= 0m)
            return RuleResult.Refuse("Enter the invoice value before sending this for approval.");

        if (!hasProject)
            return RuleResult.Refuse("Link this order to at least one project before sending it for approval.");

        return RuleResult.Allowed;
    }

    /// <summary>
    /// Whether this person may decide this request.
    ///
    /// The approver must not be the submitter. This is the rule the whole phase exists for: an
    /// approval somebody can grant themselves is a formality, not a control. Compared on the stored
    /// identifier rather than the display name, because names are not unique and can change.
    /// </summary>
    public static RuleResult CanDecide(ApprovalStatus status, string? requestedById, string? deciderId)
    {
        if (status != ApprovalStatus.Pending)
            return RuleResult.Refuse("That request has already been decided.");

        if (string.IsNullOrWhiteSpace(deciderId))
            return RuleResult.Refuse("The decision could not be attributed to anyone, so it was not recorded.");

        if (!string.IsNullOrWhiteSpace(requestedById) &&
            string.Equals(requestedById, deciderId, StringComparison.OrdinalIgnoreCase))
            return RuleResult.Refuse("You cannot approve your own submission. Someone else has to decide this one.");

        return RuleResult.Allowed;
    }

    /// <summary>A rejection needs a reason the submitter can act on.</summary>
    public static RuleResult CanReject(ApprovalStatus status, string? requestedById, string? deciderId,
                                       string? reason)
    {
        var canDecide = CanDecide(status, requestedById, deciderId);
        if (!canDecide.Ok) return canDecide;

        if ((reason ?? "").Trim().Length < MinimumRejectionReasonLength)
            return RuleResult.Refuse(
                "Say why this is being rejected, so whoever raised it knows what to change.");

        return RuleResult.Allowed;
    }

    /// <summary>
    /// Whether an approved purchase order may now be issued.
    ///
    /// The fingerprint check is the point. Approval is of a specific supplier, amount, rate and set
    /// of projects; if any of those moved after the decision, what is about to be issued is not what
    /// was approved, and it goes back to Draft rather than out of the door.
    /// </summary>
    public static RuleResult CanIssue(PoApprovalStatus status, string? approvedFingerprint,
                                      string currentFingerprint)
    {
        if (status == PoApprovalStatus.Issued)
            return RuleResult.Refuse("This purchase order has already been issued.");

        if (status != PoApprovalStatus.Approved)
            return RuleResult.Refuse("This purchase order has not been approved yet.");

        if (!SubjectFingerprint.Matches(approvedFingerprint, currentFingerprint))
            return RuleResult.Refuse(
                "This order has changed since it was approved, so it cannot be issued. " +
                "It has been returned to draft and needs approving again.");

        return RuleResult.Allowed;
    }

    /// <summary>
    /// Whether a submitter may pull a request back. Only while nobody has decided it - withdrawing
    /// something already approved would quietly undo a decision that was properly made.
    /// </summary>
    public static RuleResult CanWithdraw(PoApprovalStatus status) =>
        status == PoApprovalStatus.PendingApproval
            ? RuleResult.Allowed
            : RuleResult.Refuse("There is nothing waiting for approval on this order.");

    /// <summary>
    /// Whether a change to the order should knock an approval down.
    ///
    /// Issued counts, not just Approved and PendingApproval. It is tempting to treat an issued
    /// order as history that must not be rewritten, but leaving it Issued after a material change
    /// is worse: the system would then show Financial Director approval for an amount they never
    /// saw, which is the exact hole this whole workflow exists to close. Nothing is lost by
    /// resetting it - the approval rows and the audit trail both keep what was approved, by whom,
    /// and that it had been issued.
    ///
    /// A Draft has nothing to lose, and a Rejected order is already back with its submitter.
    /// </summary>
    public static bool EditInvalidates(PoApprovalStatus status) =>
        status is PoApprovalStatus.Approved
               or PoApprovalStatus.PendingApproval
               or PoApprovalStatus.Issued;
}
