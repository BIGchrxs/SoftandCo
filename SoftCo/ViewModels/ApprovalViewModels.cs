using SoftCo.Models;

namespace SoftCo.ViewModels;

// The approval queue and the review screen.
// Namespace deliberately matches the other view-model files.

/// <summary>One line of the Financial Director's queue, flattened in the query.</summary>
public class ApprovalRowViewModel
{
    public int Id { get; set; }
    public int? OrderId { get; set; }
    public int? InvoiceId { get; set; }

    public ApprovalKind Kind { get; set; }
    public ApprovalStatus Status { get; set; }

    public string Reference { get; set; } = "";
    public string SupplierName { get; set; } = "";

    public decimal AmountZar { get; set; }

    public DateTime RequestedAt { get; set; }
    public string? RequestedByName { get; set; }

    public DateTime? DecidedAt { get; set; }
    public string? DecidedByName { get; set; }

    /// <summary>
    /// How long this has been waiting. Shown because the cost of this system is somebody's time,
    /// and a request sitting for a week is the failure it is meant to prevent.
    /// </summary>
    public int DaysWaiting(DateTime utcNow) =>
        (int)(utcNow - RequestedAt).TotalDays;
}

/// <summary>Everything the decision screen shows.</summary>
public class ApprovalReviewViewModel
{
    public Approval Approval { get; set; } = null!;

    /// <summary>Null for a client invoice, which has no supplier order behind it.</summary>
    public SupplierOrder? Order { get; set; }

    /// <summary>Set only for a client invoice.</summary>
    public CustomerInvoice? Invoice { get; set; }

    public bool IsInvoice => Approval.Kind == ApprovalKind.CustomerInvoice;

    /// <summary>
    /// Set only for a payment release. The order is still carried, because the decision is about
    /// money owed on that order and the approver needs to see which one.
    /// </summary>
    public PaymentRequest? PaymentRequest { get; set; }

    public bool IsPaymentRelease => Approval.Kind == ApprovalKind.PaymentRelease;

    public string Projects { get; set; } = "";

    /// <summary>
    /// Whether this person may decide this request. Presentation only - the POST checks again,
    /// because hiding a button is not access control.
    /// </summary>
    public bool CanDecide { get; set; }

    /// <summary>Shown when <see cref="CanDecide"/> is false, so the screen explains itself.</summary>
    public string? WhyNot { get; set; }

    public int MinimumReasonLength { get; set; }
}
