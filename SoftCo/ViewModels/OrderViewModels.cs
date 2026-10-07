using System.ComponentModel.DataAnnotations;
using SoftCo.Models;

namespace SoftCo.ViewModels;

// Order grid, filters and the create/edit form.
// Namespace deliberately matches the other view-model files, so splitting them changed
// no controller, view or using directive.

/// <summary>Filter state for the tracker grid. Every field maps to a column in the spreadsheet.</summary>
public class OrderFilterViewModel
{
    public int? SupplierId { get; set; }
    public int? ProjectId { get; set; }
    public FulfilmentStatus? Fulfilment { get; set; }
    public SettlementStatus? Settlement { get; set; }
    public string? Search { get; set; }

    public bool IsActive =>
        SupplierId.HasValue || ProjectId.HasValue || Fulfilment.HasValue
        || Settlement.HasValue || !string.IsNullOrWhiteSpace(Search);
}

public class OrderRowViewModel
{
    public int Id { get; set; }
    public string? PoNumber { get; set; }
    public OrderType OrderType { get; set; }
    public string Supplier { get; set; } = "";
    public string Product { get; set; } = "";
    public string Projects { get; set; } = "";
    public FulfilmentStatus Fulfilment { get; set; }
    public DateOnly? CargoReadiness { get; set; }
    public DateOnly? InvoiceDate { get; set; }
    public string? InvoiceRef { get; set; }
    public string CurrencyCode { get; set; } = "";
    public decimal InvoiceValueForeign { get; set; }
    public decimal InvoiceValueZar { get; set; }
    public decimal DepositForeign { get; set; }
    public decimal SettlementForeign { get; set; }
    public decimal OutstandingZar { get; set; }
    public SettlementStatus Settlement { get; set; }
}

public class OrderListViewModel
{
    public OrderFilterViewModel Filter { get; set; } = new();
    public List<OrderRowViewModel> Rows { get; set; } = [];

    public List<(int Id, string Name)> Suppliers { get; set; } = [];
    public List<(int Id, string Name)> Projects { get; set; } = [];

    // Totals are always in Rand: the foreign columns mix CNY and USD and cannot be summed.
    public decimal TotalInvoicedZar => Rows.Sum(r => r.InvoiceValueZar);
    public decimal TotalOutstandingZar => Rows.Sum(r => r.OutstandingZar);
    public decimal TotalPaidZar => TotalInvoicedZar - TotalOutstandingZar;
    public int OutstandingCount => Rows.Count(r => r.Settlement != SettlementStatus.Paid);
}

public class OrderEditViewModel
{
    public int Id { get; set; }

    /// <summary>Read-only once allocated; shown so the user can quote it.</summary>
    public string? PoNumber { get; set; }

    /// <summary>Set by the controller, not the form - a Local order can never post itself International.</summary>
    public OrderType OrderType { get; set; } = OrderType.International;

    [Required, Display(Name = "Supplier")]
    public int SupplierId { get; set; }

    [Required, StringLength(300), Display(Name = "Product")]
    public string ProductDescription { get; set; } = "";

    [Display(Name = "Projects")]
    public List<int> ProjectIds { get; set; } = [];

    /// <summary>
    /// How the order's cost is split across its projects, keyed by project id and expressed as a
    /// percentage because that is what people type.
    ///
    /// Only meaningful - and only shown - when more than one project is selected. With a single
    /// project the share is 1 and there is nothing to decide, which is the case for most orders.
    /// </summary>
    /// <remarks>
    /// Nullable values on purpose. An empty box must bind as "not entered" rather than failing to
    /// parse - a failed bind makes the whole form invalid before any of its own rules run, and the
    /// order is then refused with no message anywhere on the page.
    /// </remarks>
    public Dictionary<int, decimal?> ProjectShares { get; set; } = [];

    /// <summary>
    /// Whether the Rand invoice value includes South African VAT. Needed for gross profit: a local
    /// invoice usually includes VAT that Soft &amp; Co reclaim, an imported one carries none.
    /// </summary>
    [Display(Name = "VAT on this invoice")]
    public SupplierVatTreatment VatTreatment { get; set; } = SupplierVatTreatment.Unknown;

    [Display(Name = "Delivery status")]
    public FulfilmentStatus FulfilmentStatus { get; set; } = FulfilmentStatus.InProduction;

    [Display(Name = "Cargo readiness")]
    public DateOnly? CargoReadinessDate { get; set; }

    [StringLength(100), Display(Name = "Invoice ref")]
    public string? InvoiceRef { get; set; }

    [Display(Name = "Invoice date")]
    public DateOnly? InvoiceDate { get; set; }

    [Required, StringLength(3), Display(Name = "Currency")]
    public string CurrencyCode { get; set; } = "CNY";

    [Range(0.000001, 1000000), Display(Name = "Exchange rate (ZAR per unit)")]
    public decimal ExchangeRate { get; set; }

    [Range(0, 1000000000), Display(Name = "Invoice value (foreign)")]
    public decimal InvoiceValueForeign { get; set; }

    [Range(0, 1000000000), Display(Name = "Invoice value (ZAR)")]
    public decimal InvoiceValueZar { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }

    public List<(int Id, string Name)> AllSuppliers { get; set; } = [];
    public List<(int Id, string Name)> AllProjects { get; set; } = [];
}
