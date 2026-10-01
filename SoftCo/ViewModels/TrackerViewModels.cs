using System.ComponentModel.DataAnnotations;
using SoftCo.Models;

namespace SoftCo.ViewModels;

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

public class RecordPaymentViewModel
{
    public int SupplierOrderId { get; set; }
    public string OrderSummary { get; set; } = "";
    public string CurrencyCode { get; set; } = "";
    public decimal ExchangeRate { get; set; }
    public decimal OutstandingForeign { get; set; }

    [Display(Name = "Payment type")]
    public PaymentKind Kind { get; set; } = PaymentKind.Deposit;

    [Range(0.01, 1000000000), Display(Name = "Amount (foreign)")]
    public decimal AmountForeign { get; set; }

    [Range(0, 1000000000), Display(Name = "Amount (ZAR)")]
    public decimal AmountZar { get; set; }

    [Display(Name = "Paid date")]
    public DateOnly? PaidDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [StringLength(200)]
    public string? Reference { get; set; }
}

public class LoginViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = "";

    public string? ReturnUrl { get; set; }
}

/// <summary>Panel shown on an order once an invoice is attached.</summary>
public class RequestPaymentViewModel
{
    public int SupplierOrderId { get; set; }
    public string PoNumber { get; set; } = "";
    public string Supplier { get; set; } = "";
    public string Product { get; set; } = "";
    public decimal OutstandingZar { get; set; }

    /// <summary>Active contacts to choose from.</summary>
    public List<PaymentContact> Contacts { get; set; } = [];

    /// <summary>Requests already sent for this order, newest first.</summary>
    public List<PaymentRequest> History { get; set; } = [];

    /// <summary>Where mail is going right now, e.g. "written to App_Data/sent-email".</summary>
    public string DeliveryDescription { get; set; } = "";

    public bool AttachInvoice { get; set; } = true;
}
