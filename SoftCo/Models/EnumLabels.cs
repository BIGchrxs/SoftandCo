namespace SoftCo.Models;

/// <summary>
/// Display names for the status enums. The fulfilment labels match the wording Soft &amp; Co.
/// already use in the tracker, so the screen reads the way the spreadsheet does.
/// </summary>
public static class EnumLabels
{
    public static string Label(this FulfilmentStatus s) => s switch
    {
        FulfilmentStatus.InProduction => "In Production",
        FulfilmentStatus.Testing => "Testing",
        FulfilmentStatus.Shipping => "Shipping",
        FulfilmentStatus.Clearance => "Clearance",
        FulfilmentStatus.Delivered => "Delivered",
        _ => s.ToString()
    };

    /// <summary>
    /// Local wording for the same stages. A local order is ordered, on its way, or delivered -
    /// it never passes through testing or customs - so the shared enum is relabelled rather
    /// than duplicated, which keeps every filter and badge working on both pages.
    /// </summary>
    public static string LocalLabel(this FulfilmentStatus s) => s switch
    {
        FulfilmentStatus.InProduction => "Ordered",
        FulfilmentStatus.Shipping => "In transit",
        FulfilmentStatus.Delivered => "Delivered",
        _ => s.Label()
    };

    /// <summary>The stages a local order can actually be in.</summary>
    public static readonly FulfilmentStatus[] LocalStages =
    [
        FulfilmentStatus.InProduction,
        FulfilmentStatus.Shipping,
        FulfilmentStatus.Delivered
    ];

    public static string Label(this SettlementStatus s) => s switch
    {
        SettlementStatus.Unpaid => "Outstanding",
        SettlementStatus.PartPaid => "Part paid",
        SettlementStatus.Paid => "Paid",
        _ => s.ToString()
    };

    public static string Label(this SupplierType t) => t switch
    {
        SupplierType.Manufacturer => "Manufacturer",
        SupplierType.ServiceProvider => "Service provider",
        _ => t.ToString()
    };

    public static string Label(this SupplierOrigin o) => o switch
    {
        SupplierOrigin.Local => "Local",
        SupplierOrigin.International => "International",
        _ => o.ToString()
    };

    public static string Label(this ReceiptMethod m) => m switch
    {
        ReceiptMethod.Eft => "EFT",
        ReceiptMethod.Card => "Card",
        ReceiptMethod.Cash => "Cash",
        ReceiptMethod.Other => "Other",
        _ => m.ToString()
    };

    public static string Label(this CreditNoteStatus s) => s switch
    {
        CreditNoteStatus.Draft => "Draft",
        CreditNoteStatus.Issued => "Issued",
        CreditNoteStatus.Cancelled => "Cancelled",
        _ => s.ToString()
    };

    public static string Label(this CustomerInvoiceStatus s) => s switch
    {
        CustomerInvoiceStatus.Draft => "Draft",
        CustomerInvoiceStatus.PendingApproval => "Awaiting approval",
        CustomerInvoiceStatus.Approved => "Approved to issue",
        CustomerInvoiceStatus.Issued => "Issued",
        CustomerInvoiceStatus.PartPaid => "Part paid",
        CustomerInvoiceStatus.Paid => "Paid",
        CustomerInvoiceStatus.Rejected => "Rejected",
        CustomerInvoiceStatus.Cancelled => "Cancelled",
        _ => s.ToString()
    };

    public static string Label(this PaymentRequestStatus s) => s switch
    {
        PaymentRequestStatus.Sent => "Sent",
        PaymentRequestStatus.Failed => "Failed",
        PaymentRequestStatus.NotSent => "Not sent",
        _ => s.ToString()
    };

    public static string Label(this PaymentReleaseStatus s) => s switch
    {
        PaymentReleaseStatus.PendingApproval => "Awaiting FD approval",
        PaymentReleaseStatus.Approved => "Approved to release",
        PaymentReleaseStatus.Rejected => "Rejected",
        PaymentReleaseStatus.Released => "Released",
        PaymentReleaseStatus.Withdrawn => "Withdrawn",
        _ => s.ToString()
    };

    public static string Label(this PoApprovalStatus s) => s switch
    {
        PoApprovalStatus.Draft => "Draft",
        PoApprovalStatus.PendingApproval => "Awaiting approval",
        PoApprovalStatus.Approved => "Approved",
        PoApprovalStatus.Issued => "Issued",
        PoApprovalStatus.Rejected => "Rejected",
        _ => s.ToString()
    };

    public static string Label(this ApprovalKind k) => k switch
    {
        ApprovalKind.PurchaseOrder => "Purchase order",
        ApprovalKind.PaymentRelease => "Payment release",
        ApprovalKind.CustomerInvoice => "Client invoice",
        _ => k.ToString()
    };

    public static string Label(this ApprovalStatus s) => s switch
    {
        ApprovalStatus.Pending => "Awaiting decision",
        ApprovalStatus.Approved => "Approved",
        ApprovalStatus.Rejected => "Rejected",
        ApprovalStatus.Withdrawn => "Withdrawn",
        _ => s.ToString()
    };

    public static string Label(this DocumentKind k) => k switch
    {
        DocumentKind.Invoice => "Invoice",
        DocumentKind.PurchaseOrder => "Purchase order",
        DocumentKind.Other => "Other",
        _ => k.ToString()
    };

    public static string Label(this VatTreatment t) => t switch
    {
        VatTreatment.Standard => "Standard rate",
        VatTreatment.ZeroRated => "Zero-rated",
        VatTreatment.Exempt => "Exempt",
        _ => t.ToString()
    };

    public static string Label(this SyncStatus s) => s switch
    {
        SyncStatus.NotSynced => "Not synced",
        SyncStatus.Pending => "Sync pending",
        SyncStatus.Synced => "Synced",
        SyncStatus.Failed => "Sync failed",
        _ => s.ToString()
    };

    public static string Label(this PaymentKind k) => k switch
    {
        PaymentKind.Deposit => "Deposit",
        PaymentKind.Settlement => "Settlement",
        _ => k.ToString()
    };
}
