namespace SoftCo.Models;

/// <summary>
/// Physical progress of goods, mirroring the five stages Soft &amp; Co already track by hand in
/// International Payment Tracker.xlsx ("1. In Production" ... "5. Delivered"). Deliberately kept
/// separate from any future purchase-order *approval* status: this column answers "where are the
/// goods?", not "has the order been signed off?".
/// </summary>
public enum FulfilmentStatus
{
    InProduction = 1,
    Testing      = 2,
    Shipping     = 3,
    Clearance    = 4,
    Delivered    = 5
}

/// <summary>
/// Derived from the payments recorded against an order — never typed in by hand. The source
/// spreadsheet has a manual "Settlement Status" column that drifts out of step with the deposit
/// and settlement figures beside it (at least one row shows part-payments totalling less than the
/// invoice while still reading "Outstanding"); computing it removes that whole class of error.
/// </summary>
public enum SettlementStatus
{
    Unpaid   = 0,
    PartPaid = 1,
    Paid     = 2
}

/// <summary>
/// Suppliers and freight/clearing agents are tracked identically in the spreadsheet (the
/// "Service Provider" sheet uses the same columns as "Suppliers"), so they share one table and
/// are separated by this discriminator rather than duplicated into two.
/// </summary>
public enum SupplierType
{
    Manufacturer    = 0,
    ServiceProvider = 1
}

/// <summary>
/// Soft &amp; Co pay suppliers in two instalments. The split is not fixed — the live tracker shows
/// both 50/50 and 30/70 arrangements — so amounts are recorded per payment rather than derived
/// from a percentage.
/// </summary>
public enum PaymentKind
{
    Deposit    = 0,
    Settlement = 1
}

/// <summary>
/// Separates the two procurement models the brief describes. International orders carry a
/// foreign currency, an exchange rate and a shipping pipeline; local orders are bought in ZAR
/// and never clear customs. Both live in one table so payments, project links, settlement maths
/// and the audit trail are written once rather than twice.
/// </summary>
public enum OrderType
{
    International = 0,
    Local         = 1
}

/// <summary>
/// What a file attached to an order actually is. Only an Invoice unlocks the request-payment
/// action - asking finance to pay against a packing list would be meaningless.
/// </summary>
public enum DocumentKind
{
    Invoice = 0,
    Other   = 1
}

/// <summary>
/// Outcome of a single attempt to email a payment request. Failures are kept rather than
/// discarded so "we asked them" can never be claimed for a message that never left.
/// </summary>
public enum PaymentRequestStatus
{
    Sent   = 0,
    Failed = 1
}

/// <summary>
/// Where a supplier sits relative to Soft &amp; Co. International suppliers invoice in a foreign
/// currency and their goods ship and clear customs; local suppliers invoice in Rand and deliver
/// by road.
///
/// Ordered so International is 0, matching <see cref="OrderType"/> and meaning the existing
/// supplier register - every one of them a Chinese manufacturer - lands on the correct value
/// without a data fix.
/// </summary>
public enum SupplierOrigin
{
    International = 0,
    Local         = 1
}
