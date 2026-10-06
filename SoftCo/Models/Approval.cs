using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftCo.Models;

/// <summary>
/// One request for a decision, and the decision itself.
///
/// <para><b>Why one table rather than three.</b> Purchase orders, payment releases and client
/// invoices all go to the same person and are judged the same way, so the Financial Director's
/// queue is one indexed query rather than a three-way UNION, and the rules - the approver is not
/// the submitter, a rejection needs a reason - are written once.</para>
///
/// <para><b>Why three nullable foreign keys rather than a SubjectType/SubjectId pair.</b> The
/// polymorphic shape loses referential integrity at exactly the point it matters: nothing would
/// stop an approval pointing at an order that no longer exists, and no constraint could catch it.
/// Here the database enforces both that the subject exists and - through a check constraint - that
/// there is exactly one of it.</para>
///
/// <para>An approval is evidence. Every subject key is <c>Restrict</c>, nothing is ever updated in
/// place once decided, and a rejected request is kept rather than replaced when the work is
/// resubmitted.</para>
/// </summary>
public class Approval
{
    public int Id { get; set; }

    public ApprovalKind Kind { get; set; }

    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

    // --- Subject -----------------------------------------------------------------------------
    // Exactly one of these is set, enforced by a check constraint (num_nonnulls = 1) rather than by
    // trusting the code that writes them.

    public int? SupplierOrderId { get; set; }
    public SupplierOrder? SupplierOrder { get; set; }

    public int? PaymentRequestId { get; set; }
    public PaymentRequest? PaymentRequest { get; set; }

    /// <summary>
    /// The third subject. The column and the check constraint predate the invoice table, so adding
    /// it was a foreign key rather than a rewrite of the constraint every existing row is validated
    /// against.
    /// </summary>
    public int? CustomerInvoiceId { get; set; }
    public CustomerInvoice? CustomerInvoice { get; set; }

    // --- What was being approved -------------------------------------------------------------

    /// <summary>
    /// The Rand figure the approver was actually shown. Stored rather than read back from the
    /// order, because the whole point of an approval is that it refers to a specific amount at a
    /// specific moment - "they approved R318,622.26" must stay true after somebody edits the order.
    /// </summary>
    [Column(TypeName = "numeric(18,2)")]
    public decimal AmountZarAtRequest { get; set; }

    /// <summary>
    /// A hash of the material facts at the moment of submission - supplier, currency, rate, values
    /// and the linked projects. Checked again before the purchase order can be issued, so an order
    /// edited after approval has to be approved again rather than quietly going out with terms
    /// nobody signed off.
    /// </summary>
    [StringLength(64)]
    public string? SubjectFingerprint { get; set; }

    // --- Who asked --------------------------------------------------------------------------
    // Id plus a name snapshot, with no foreign key to the user table, exactly as PaymentRequest
    // does: deleting an account must not erase who asked for what.

    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    [StringLength(450)] public string? RequestedById { get; set; }
    [StringLength(256)] public string? RequestedByName { get; set; }

    [StringLength(1000)]
    public string? RequestNote { get; set; }

    // --- Who decided -------------------------------------------------------------------------

    public DateTime? DecidedAt { get; set; }
    [StringLength(450)] public string? DecidedById { get; set; }
    [StringLength(256)] public string? DecidedByName { get; set; }

    /// <summary>
    /// Required on a rejection and optional on an approval. A rejection with no reason sends the
    /// work back to someone who has no idea what to change, which turns into a conversation nobody
    /// records.
    /// </summary>
    [StringLength(1000)]
    public string? DecisionReason { get; set; }

    // --- Notification -------------------------------------------------------------------------
    // Whether the request actually reached the approver. Kept for the same reason PaymentRequest
    // keeps its failures: "I sent it for approval" must not stand for an email that never left.

    public bool NotificationSent { get; set; }
    public DateTime? NotifiedAt { get; set; }
    [StringLength(256)] public string? NotifiedTo { get; set; }
    [StringLength(1000)] public string? NotificationError { get; set; }

    /// <summary>True while this request is still waiting on somebody.</summary>
    [NotMapped]
    public bool IsOpen => Status == ApprovalStatus.Pending;
}
