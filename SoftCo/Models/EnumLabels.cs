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
