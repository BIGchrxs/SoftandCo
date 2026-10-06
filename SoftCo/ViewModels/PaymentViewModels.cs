using System.ComponentModel.DataAnnotations;
using SoftCo.Models;

namespace SoftCo.ViewModels;

// Recording a payment, and asking someone to make one.
// Namespace deliberately matches the other view-model files, so splitting them changed
// no controller, view or using directive.

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

// --- User administration -------------------------------------------------------------------
