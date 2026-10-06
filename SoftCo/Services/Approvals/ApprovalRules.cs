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

    // --- Client invoices ------------------------------------------------------------------------

    /// <summary>
    /// Whether an invoice is complete enough to put to the Financial Director.
    ///
    /// A zero-total invoice is allowed through deliberately - a fully zero-rated export is a real
    /// invoice with real VAT consequences - but one with no lines at all is not a document anybody
    /// can make a decision about.
    /// </summary>
    public static RuleResult CanSubmitInvoice(CustomerInvoiceStatus status, bool hasClient, int lineCount)
    {
        if (status == CustomerInvoiceStatus.PendingApproval)
            return RuleResult.Refuse("This invoice is already waiting for approval.");

        if (status is CustomerInvoiceStatus.Issued or CustomerInvoiceStatus.PartPaid
                   or CustomerInvoiceStatus.Paid)
            return RuleResult.Refuse("This invoice has already been issued.");

        if (status == CustomerInvoiceStatus.Cancelled)
            return RuleResult.Refuse("This invoice has been cancelled.");

        if (!hasClient)
            return RuleResult.Refuse("Choose the client before sending this for approval.");

        if (lineCount == 0)
            return RuleResult.Refuse("Add at least one line before sending this for approval.");

        return RuleResult.Allowed;
    }

    /// <summary>
    /// Whether an approved invoice may be issued.
    ///
    /// Issuing allocates a number from a series a tax authority expects to be unbroken, and makes
    /// the document immutable. It is the point of no return, so it happens once and only from
    /// Approved.
    /// </summary>
    public static RuleResult CanIssueInvoice(CustomerInvoiceStatus status, decimal grandTotal)
    {
        if (status is CustomerInvoiceStatus.Issued or CustomerInvoiceStatus.PartPaid
                   or CustomerInvoiceStatus.Paid)
            return RuleResult.Refuse("This invoice has already been issued.");

        if (status != CustomerInvoiceStatus.Approved)
            return RuleResult.Refuse("This invoice has not been approved yet.");

        if (grandTotal < 0m)
            return RuleResult.Refuse("An invoice cannot total a negative amount. Use a credit note instead.");

        return RuleResult.Allowed;
    }

    /// <summary>
    /// Whether an invoice may still be edited. Issued invoices are tax documents: the VAT Act
    /// requires a credit note to reverse one, and letting staff edit them is precisely how this
    /// system would quietly become the spreadsheet it replaced.
    /// </summary>
    public static RuleResult CanEditInvoice(CustomerInvoiceStatus status) => status switch
    {
        CustomerInvoiceStatus.Draft or CustomerInvoiceStatus.Rejected => RuleResult.Allowed,
        CustomerInvoiceStatus.PendingApproval =>
            RuleResult.Refuse("This invoice is waiting for approval. Withdraw it first if it needs changing."),
        CustomerInvoiceStatus.Approved =>
            RuleResult.Refuse("This invoice has been approved. Withdraw it first if it needs changing."),
        CustomerInvoiceStatus.Cancelled => RuleResult.Refuse("This invoice has been cancelled."),
        _ => RuleResult.Refuse("An issued invoice cannot be changed. Raise a credit note instead.")
    };

    // --- Payment release ----------------------------------------------------------------------

    /// <summary>
    /// Whether there is anything to ask the Financial Director to release.
    ///
    /// The invoice gate is the same one the old direct-send flow applied, and for the same reason:
    /// asking finance to pay against a packing list is meaningless. What is new is that nothing is
    /// emailed on the strength of this check alone.
    /// </summary>
    public static RuleResult CanRaiseRelease(bool hasInvoice, decimal outstandingZar, bool hasContact,
                                             bool alreadyPending)
    {
        if (alreadyPending)
            return RuleResult.Refuse("A payment release for this order is already waiting for approval.");

        if (!hasInvoice)
            return RuleResult.Refuse("Attach the supplier invoice before requesting payment.");

        if (!hasContact)
            return RuleResult.Refuse("Choose who the request should go to.");

        if (outstandingZar <= 0m)
            return RuleResult.Refuse("There is nothing outstanding on this order to pay.");

        return RuleResult.Allowed;
    }

    /// <summary>
    /// Whether an approved payment release may now actually be sent.
    ///
    /// The amount is checked again against what is outstanding now. The Financial Director approved
    /// releasing a specific figure; if a payment was recorded in between, that figure is no longer
    /// what would be asked for, and the request goes back for approval rather than out of the door.
    /// Serves the same purpose for money as the fingerprint does for a purchase order.
    /// </summary>
    public static RuleResult CanRelease(PaymentReleaseStatus status, decimal approvedAmountZar,
                                        decimal outstandingNowZar)
    {
        if (status == PaymentReleaseStatus.Released)
            return RuleResult.Refuse("That payment request has already been sent.");

        if (status != PaymentReleaseStatus.Approved)
            return RuleResult.Refuse("That payment release has not been approved yet.");

        if (approvedAmountZar != outstandingNowZar)
            return RuleResult.Refuse(
                $"The outstanding balance has changed from R {approvedAmountZar:N2} to " +
                $"R {outstandingNowZar:N2} since this was approved, so it cannot be sent. " +
                "Raise it again for the amount that is actually owing.");

        return RuleResult.Allowed;
    }

    /// <summary>
    /// Whether a raised payment release may be pulled back. Only while nobody has decided it, for
    /// the same reason a purchase order cannot be withdrawn after approval.
    /// </summary>
    public static RuleResult CanWithdrawRelease(PaymentReleaseStatus status) =>
        status == PaymentReleaseStatus.PendingApproval
            ? RuleResult.Allowed
            : RuleResult.Refuse("There is nothing waiting for approval on this payment request.");

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
