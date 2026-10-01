using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftCo.Models;

/// <summary>
/// One line of the International Payment Tracker: what was ordered from whom, for which
/// project(s), where it is physically, what it was invoiced at, and what has been paid.
/// </summary>
public class SupplierOrder
{
    public int Id { get; set; }

    /// <summary>
    /// Internal purchase-order reference, e.g. PO-2026-0001. Generated from a database sequence
    /// when the order is first saved and never changed afterwards: it is the one identifier
    /// Soft &amp; Co. own, as opposed to <see cref="InvoiceRef"/>, which belongs to the supplier
    /// and can be reissued by them at any time.
    /// </summary>
    [StringLength(20)]
    public string? PoNumber { get; set; }

    /// <summary>International (foreign currency, shipped) or Local (ZAR, no clearance).</summary>
    public OrderType OrderType { get; set; } = OrderType.International;

    public int SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    /// <summary>
    /// Free text for this first slice ("Lighting", "Coffee Table", "Outdoor Furn", "Smart
    /// Toilets"). Becomes a foreign key to the product catalogue once the SKU module is built.
    /// </summary>
    [Required, StringLength(300)]
    public string ProductDescription { get; set; } = string.Empty;

    public FulfilmentStatus FulfilmentStatus { get; set; } = FulfilmentStatus.InProduction;

    /// <summary>Date the supplier expects cargo to be ready ("Cargo Readiness" in the sheet).</summary>
    public DateOnly? CargoReadinessDate { get; set; }

    [StringLength(100)]
    public string? InvoiceRef { get; set; }

    public DateOnly? InvoiceDate { get; set; }

    // --- Money -------------------------------------------------------------------------------
    // Every amount is decimal, never double, and mapped to Postgres numeric (see AppDbContext).
    // Rounding drift here shows up as invoices that do not reconcile.

    /// <summary>ISO code of the currency the supplier actually invoiced in - CNY or USD today.</summary>
    [Required, StringLength(3)]
    public string CurrencyCode { get; set; } = "CNY";

    /// <summary>
    /// Rand per one unit of <see cref="CurrencyCode"/>, fixed at capture time rather than looked
    /// up live. The tracker's Rand column is the rate agreed for that invoice and must not change
    /// retrospectively when the market moves.
    /// </summary>
    [Column(TypeName = "numeric(18,6)")]
    public decimal ExchangeRate { get; set; }

    [Column(TypeName = "numeric(18,2)")]
    public decimal InvoiceValueForeign { get; set; }

    /// <summary>
    /// Rand value of the invoice. Normally ExchangeRate x InvoiceValueForeign, but stored rather
    /// than computed so a hand-agreed Rand figure stays exactly as finance recorded it.
    /// </summary>
    [Column(TypeName = "numeric(18,2)")]
    public decimal InvoiceValueZar { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [StringLength(450)] public string? CreatedById { get; set; }
    public DateTime? UpdatedAt { get; set; }
    [StringLength(450)] public string? UpdatedById { get; set; }

    public ICollection<OrderProject> OrderProjects { get; set; } = new List<OrderProject>();
    public ICollection<OrderPayment> Payments { get; set; } = new List<OrderPayment>();
    public ICollection<OrderDocument> Documents { get; set; } = new List<OrderDocument>();
    public ICollection<PaymentRequest> PaymentRequests { get; set; } = new List<PaymentRequest>();

    // --- Derived -----------------------------------------------------------------------------

    /// <summary>Total paid in the invoice currency. Requires Payments to be loaded.</summary>
    [NotMapped]
    public decimal PaidForeign => Payments.Sum(p => p.AmountForeign);

    [NotMapped]
    public decimal OutstandingForeign => InvoiceValueForeign - PaidForeign;

    [NotMapped]
    public decimal PaidZar => Payments.Sum(p => p.AmountZar);

    [NotMapped]
    public decimal OutstandingZar => InvoiceValueZar - PaidZar;

    /// <summary>
    /// Never stored. A settlement status that can disagree with the payments beside it is exactly
    /// the spreadsheet failure this system exists to remove.
    /// </summary>
    /// <summary>
    /// Gates the request-payment action: there is no point asking finance to pay until the
    /// supplier's invoice is actually on the record. Requires Documents to be loaded.
    /// </summary>
    [NotMapped]
    public bool HasInvoice => Documents.Any(d => d.Kind == DocumentKind.Invoice);

    [NotMapped]
    public SettlementStatus SettlementStatus =>
        InvoiceValueForeign <= 0m || PaidForeign <= 0m ? SettlementStatus.Unpaid
        : PaidForeign >= InvoiceValueForeign ? SettlementStatus.Paid
        : SettlementStatus.PartPaid;
}
