using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftCo.Models;

/// <summary>
/// A credit note against an issued invoice.
///
/// <para><b>Not optional, and not a convenience.</b> The VAT Act requires a credit note to reverse a
/// tax invoice, and an issued invoice in this system is immutable. Without this, the only way to
/// correct a wrong invoice would be to edit it - and a system where staff edit issued tax invoices
/// is the spreadsheet this one replaced, with better fonts.</para>
///
/// <para>The total credited against an invoice can never exceed what was charged. That cap is
/// enforced in <see cref="Services.Invoicing.ReceivableMath.CanCredit"/>, because crediting more
/// than was ever invoiced manufactures a refund out of nothing, in a document SARS will read.</para>
/// </summary>
public class CreditNote
{
    public int Id { get; set; }

    /// <summary>
    /// Null while drafting, allocated from its own sequence on issue. Credit notes draw from
    /// <c>credit_note_number_seq</c> rather than sharing the invoice series: gaps in either series
    /// would otherwise be unexplainable to anyone reading the books.
    /// </summary>
    [StringLength(20)]
    public string? CreditNoteNumber { get; set; }

    /// <summary>
    /// The invoice being credited. <c>Restrict</c>, like every other piece of financial evidence
    /// here: a credited invoice is part of the record and cannot be deleted away from the note that
    /// reverses it.
    /// </summary>
    public int CustomerInvoiceId { get; set; }
    public CustomerInvoice? CustomerInvoice { get; set; }

    public CreditNoteStatus Status { get; set; } = CreditNoteStatus.Draft;

    /// <summary>
    /// Why this is being credited, printed on the document.
    ///
    /// Required, and required to say something. A credit note with no reason is a reduction in
    /// revenue that nobody can account for afterwards, which is exactly the question an auditor
    /// asks first.
    /// </summary>
    [Required, StringLength(1000)]
    public string Reason { get; set; } = string.Empty;

    public DateOnly? IssueDate { get; set; }

    [Column(TypeName = "numeric(18,2)")] public decimal NetTotal { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal VatTotal { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal GrandTotal { get; set; }

    [StringLength(450)] public string? IssuedById { get; set; }
    [StringLength(256)] public string? IssuedByName { get; set; }

    [StringLength(100)] public string? ExternalId { get; set; }
    [StringLength(100)] public string? ExternalReference { get; set; }
    public SyncStatus SyncStatus { get; set; } = SyncStatus.NotSynced;
    public DateTime? LastSyncedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [StringLength(450)] public string? CreatedById { get; set; }

    public ICollection<CreditNoteLine> Lines { get; set; } = new List<CreditNoteLine>();

    /// <summary>
    /// True once this is a tax document in its own right. Only an issued credit note reduces what
    /// the client owes; a draft is somebody thinking aloud.
    /// </summary>
    [NotMapped]
    public bool IsIssued => Status == CreditNoteStatus.Issued;
}

/// <summary>
/// One line of a credit note.
///
/// Carries its own VAT rate, frozen at save, exactly as an invoice line does - and for a sharper
/// reason: a credit note reverses a specific invoice, so it must carry the rate <i>that invoice</i>
/// was issued at, not whatever the rate happens to be on the day the mistake is noticed.
/// </summary>
public class CreditNoteLine
{
    public int Id { get; set; }

    public int CreditNoteId { get; set; }
    public CreditNote? CreditNote { get; set; }

    public int Sort { get; set; }

    [Required, StringLength(500)]
    public string Description { get; set; } = string.Empty;

    [Column(TypeName = "numeric(18,3)")]
    public decimal Quantity { get; set; } = 1m;

    [Column(TypeName = "numeric(18,2)")]
    public decimal UnitPriceExclVat { get; set; }

    public VatTreatment VatTreatment { get; set; } = VatTreatment.Standard;

    [Column(TypeName = "numeric(5,2)")]
    public decimal VatRatePercent { get; set; }

    [Column(TypeName = "numeric(18,2)")] public decimal LineNetExclVat { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal LineVatAmount { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal LineTotalInclVat { get; set; }
}
