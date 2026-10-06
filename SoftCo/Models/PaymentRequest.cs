using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftCo.Models;

/// <summary>
/// A request that a named person pay a specific supplier invoice.
///
/// <para>Raised by staff, approved by the Financial Director, released by staff. The row is written
/// when the request is <b>raised</b> - before anything is emailed - so that what the approver agreed
/// to is recorded independently of whether a message later got out.</para>
///
/// <para>Two statuses, deliberately. <see cref="ReleaseStatus"/> says how far the approval has got;
/// <see cref="Status"/> says whether the email left. A failed send is kept rather than discarded,
/// because "we asked them" must never stand for a message that never arrived.</para>
/// </summary>
public class PaymentRequest
{
    public int Id { get; set; }

    public int SupplierOrderId { get; set; }
    public SupplierOrder? SupplierOrder { get; set; }

    public int PaymentContactId { get; set; }
    public PaymentContact? PaymentContact { get; set; }

    /// <summary>
    /// Where this sits in the approval workflow. Nothing is emailed until it reaches
    /// <see cref="PaymentReleaseStatus.Released"/>.
    /// </summary>
    public PaymentReleaseStatus ReleaseStatus { get; set; } = PaymentReleaseStatus.PendingApproval;

    /// <summary>
    /// Whether the email left. Means nothing until the request has been released, which is what
    /// <see cref="PaymentRequestStatus.NotSent"/> says rather than defaulting to a hopeful "Sent".
    /// </summary>
    public PaymentRequestStatus Status { get; set; } = PaymentRequestStatus.NotSent;

    [StringLength(300)]
    public string? Subject { get; set; }

    /// <summary>Populated only on failure. Never contains credentials or the message body.</summary>
    [StringLength(1000)]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Amount outstanding at the moment the request was raised, in Rand. This is the figure put in
    /// front of the approver, so it is checked again at release: if a payment has been recorded in
    /// between, the approval no longer describes what is about to be asked for.
    /// </summary>
    [Column(TypeName = "numeric(18,2)")]
    public decimal AmountZarAtRequest { get; set; }

    /// <summary>
    /// Whether to attach the supplier invoice when the email finally goes. Chosen when the request
    /// is raised, so release honours what was actually approved rather than re-asking.
    /// </summary>
    public bool AttachInvoice { get; set; } = true;

    // --- Raised ------------------------------------------------------------------------------

    public DateTime RaisedAt { get; set; } = DateTime.UtcNow;
    [StringLength(450)] public string? RaisedById { get; set; }
    [StringLength(256)] public string? RaisedByName { get; set; }

    // --- Released ----------------------------------------------------------------------------
    // Nullable: these describe an email, and until one is attempted there is nothing to describe.
    // A non-null SentAt on a request nobody has sent would be a small, quiet lie in a table whose
    // whole purpose is to be believable afterwards.

    public DateTime? SentAt { get; set; }
    [StringLength(450)] public string? SentById { get; set; }
    [StringLength(256)] public string? SentByName { get; set; }

    /// <summary>True while this request is still waiting on the Financial Director.</summary>
    [NotMapped]
    public bool IsAwaitingApproval => ReleaseStatus == PaymentReleaseStatus.PendingApproval;

    /// <summary>True once approved and not yet sent - the state where staff must act.</summary>
    [NotMapped]
    public bool IsReadyToRelease => ReleaseStatus == PaymentReleaseStatus.Approved;
}
