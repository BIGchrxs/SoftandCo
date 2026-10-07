using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftCo.Models;

/// <summary>
/// An invoice Soft &amp; Co issue to a client.
///
/// <para><b>Never named <c>Invoice</c>.</b> <see cref="SupplierOrder.InvoiceRef"/>,
/// <see cref="SupplierOrder.InvoiceDate"/> and <see cref="SupplierOrder.HasInvoice"/> all already
/// mean the <i>supplier's</i> invoice to Soft &amp; Co - money going out. A bare <c>Invoice</c> type
/// beside them would be a permanent source of wrong-direction bugs, and the direction is the whole
/// point: this is the revenue half of gross profit.</para>
///
/// <para>Issued invoices are immutable. Correcting one is a credit note, which is also what the VAT
/// Act requires - editing an issued tax invoice is how this system would quietly become the
/// spreadsheet it replaced.</para>
/// </summary>
public class CustomerInvoice
{
    public int Id { get; set; }

    /// <summary>
    /// Null while the invoice is a draft, allocated from a sequence when it is issued, and never
    /// changed afterwards. The same pattern - and the same filtered unique index - as
    /// <see cref="SupplierOrder.PoNumber"/>: a draft that is abandoned must not burn a number out of
    /// a series a tax authority expects to be unbroken.
    /// </summary>
    [StringLength(20)]
    public string? InvoiceNumber { get; set; }

    public int ClientId { get; set; }
    public Client? Client { get; set; }

    /// <summary>
    /// Which job this is for. Nullable because the project register still has unassigned rows, and
    /// because not every invoice belongs to a project. Gross profit can only count an invoice that
    /// has one.
    /// </summary>
    public int? ProjectId { get; set; }
    public Project? Project { get; set; }

    /// <summary>
    /// Stored, not derived. The grid filters on it, and a computed status would force that filter
    /// into memory - which is the trap <c>OrdersController.Index</c> is already caught in.
    /// </summary>
    public CustomerInvoiceStatus Status { get; set; } = CustomerInvoiceStatus.Draft;

    /// <summary>Set when the invoice is issued. A draft has not been dated yet.</summary>
    public DateOnly? IssueDate { get; set; }

    /// <summary>
    /// What the client is actually held to. Calculated from the client's default terms at issue and
    /// then stored, so changing a client's terms never moves a date already agreed.
    /// </summary>
    public DateOnly? DueDate { get; set; }

    [Range(0, 365)]
    public int PaymentTermsDays { get; set; } = 30;

    /// <summary>The client's own order or reference number, printed so they can match it up.</summary>
    [StringLength(100)]
    public string? ClientReference { get; set; }

    // --- Totals ------------------------------------------------------------------------------
    // Stored rather than computed on read, so an issued invoice re-renders identically forever even
    // if a rate changes or the arithmetic is improved. Always the sum of the lines - see
    // Services/Invoicing/InvoiceMath.cs, which is the only thing that calculates them.

    [Column(TypeName = "numeric(18,2)")] public decimal NetTotal { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal VatTotal { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal GrandTotal { get; set; }

    /// <summary>
    /// Whether the person entering this typed VAT-inclusive prices. Affects the entry form only:
    /// inclusive prices are converted once at save and stored exclusive, because repeatedly deriving
    /// exclusive from a 2dp inclusive figure loses cents.
    /// </summary>
    public bool PricesEnteredInclusive { get; set; }

    /// <summary>Printed on the invoice.</summary>
    [StringLength(2000)]
    public string? Notes { get; set; }

    /// <summary>Never printed. For whoever picks this up next.</summary>
    [StringLength(2000)]
    public string? InternalNotes { get; set; }

    // --- Issued ------------------------------------------------------------------------------
    // Snapshots rather than foreign keys, as approvals and payment requests use: a deleted account
    // must not erase who issued an invoice.

    [StringLength(450)] public string? IssuedById { get; set; }
    [StringLength(256)] public string? IssuedByName { get; set; }

    // --- External accounting system ----------------------------------------------------------
    [StringLength(100)] public string? ExternalId { get; set; }
    [StringLength(100)] public string? ExternalReference { get; set; }
    public SyncStatus SyncStatus { get; set; } = SyncStatus.NotSynced;
    public DateTime? LastSyncedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [StringLength(450)] public string? CreatedById { get; set; }

    public ICollection<CustomerInvoiceLine> Lines { get; set; } = new List<CustomerInvoiceLine>();

    /// <summary>
    /// Every request for a decision this invoice has been through. An invoice is approved before it
    /// is issued, by the same person and through the same queue as a purchase order.
    /// </summary>
    public ICollection<Approval> Approvals { get; set; } = new List<Approval>();

    /// <summary>Money received against this invoice. Requires loading to use the balances below.</summary>
    public ICollection<InvoiceReceipt> Receipts { get; set; } = new List<InvoiceReceipt>();

    /// <summary>Credit notes raised against this invoice, issued or not.</summary>
    public ICollection<CreditNote> CreditNotes { get; set; } = new List<CreditNote>();

    // --- Derived -----------------------------------------------------------------------------

    /// <summary>
    /// Computed, never stored. Overdue is a function of today, so a stored flag starts drifting the
    /// moment it is written and is wrong by morning.
    /// </summary>
    public bool IsOverdue(DateOnly today) =>
        DueDate is DateOnly due
        && due < today
        && Status is CustomerInvoiceStatus.Issued or CustomerInvoiceStatus.PartPaid;

    /// <summary>
    /// Total received. Requires <see cref="Receipts"/> to be loaded.
    /// </summary>
    [NotMapped]
    public decimal ReceivedTotal => Receipts.Sum(r => r.AmountZar);

    /// <summary>
    /// Total credited. Only issued credit notes count - a draft is somebody thinking aloud, and
    /// counting it would understate what the client owes. Requires <see cref="CreditNotes"/> to be
    /// loaded.
    /// </summary>
    [NotMapped]
    public decimal CreditedTotal =>
        CreditNotes.Where(c => c.Status == CreditNoteStatus.Issued).Sum(c => c.GrandTotal);

    /// <summary>
    /// What is still to be collected. Negative when the client has overpaid, deliberately: that is
    /// money owed back, and a zero would hide it.
    /// </summary>
    [NotMapped]
    public decimal OutstandingZar => GrandTotal - CreditedTotal - ReceivedTotal;

    /// <summary>Whether this invoice still counts as money the client owes.</summary>
    [NotMapped]
    public bool IsOutstanding =>
        Status is CustomerInvoiceStatus.Issued or CustomerInvoiceStatus.PartPaid;

    /// <summary>
    /// True once the invoice is a tax document rather than a working draft. Nothing about an issued
    /// invoice may be edited.
    /// </summary>
    [NotMapped]
    public bool IsIssued =>
        Status is CustomerInvoiceStatus.Issued or CustomerInvoiceStatus.PartPaid
               or CustomerInvoiceStatus.Paid;
}

/// <summary>
/// One line of a client invoice.
///
/// VAT lives here, not on the header. One invoice can legitimately carry both treatments:
/// T.M Mauritius is offshore, so exported goods are zero-rated while local delivery on the same job
/// is standard-rated, and a single header rate could not represent that.
/// </summary>
public class CustomerInvoiceLine
{
    public int Id { get; set; }

    public int CustomerInvoiceId { get; set; }
    public CustomerInvoice? CustomerInvoice { get; set; }

    /// <summary>Order on the printed invoice. Not the primary key, so lines can be reordered.</summary>
    public int Sort { get; set; }

    [Required, StringLength(500)]
    public string Description { get; set; } = string.Empty;

    [Column(TypeName = "numeric(18,3)")]
    public decimal Quantity { get; set; } = 1m;

    /// <summary>
    /// Always exclusive of VAT, whatever was typed. Inclusive entry is converted once at save.
    /// </summary>
    [Column(TypeName = "numeric(18,2)")]
    public decimal UnitPriceExclVat { get; set; }

    public VatTreatment VatTreatment { get; set; } = VatTreatment.Standard;

    /// <summary>
    /// The rate actually applied, frozen on the line. Not read from configuration at render time:
    /// South Africa moved from 14% to 15% in 2018, and a rate read live would silently re-price
    /// every historical invoice the next time it changed.
    /// </summary>
    [Column(TypeName = "numeric(5,2)")]
    public decimal VatRatePercent { get; set; }

    // Stored so an issued invoice re-renders to the cent forever, and so the header can be the sum
    // of the rows rather than a parallel calculation that might disagree with them.
    [Column(TypeName = "numeric(18,2)")] public decimal LineNetExclVat { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal LineVatAmount { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal LineTotalInclVat { get; set; }
}
