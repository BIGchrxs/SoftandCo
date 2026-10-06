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
///
/// <see cref="PurchaseOrder"/> is a document Soft &amp; Co generated, not one someone uploaded. It
/// is safe to store beside the others precisely because the request-payment gate tests
/// <c>== Invoice</c> rather than <c>!= Other</c>: a stored purchase order must never be mistaken
/// for the supplier's invoice and unlock a payment request against a document Soft &amp; Co wrote
/// themselves. There is a unit test asserting exactly that, so a later refactor cannot regress it.
/// </summary>
public enum DocumentKind
{
    Invoice       = 0,
    Other         = 1,
    PurchaseOrder = 2
}

/// <summary>
/// Whether the email actually left. Failures are kept rather than discarded, so "we asked them"
/// can never be claimed for a message that never went.
///
/// This answers one question and must not be made to answer another. Since payment releases need
/// approval first, a request now exists before any email is attempted - hence
/// <see cref="NotSent"/>, which is still an answer to "did it leave?" rather than an overload.
/// Where the request has got to in the approval workflow is
/// <see cref="PaymentRequest.ReleaseStatus"/>, a separate field.
///
/// NotSent is 2 rather than 0 because Sent and Failed are already written to the database and must
/// never be renumbered.
/// </summary>
public enum PaymentRequestStatus
{
    Sent    = 0,
    Failed  = 1,
    NotSent = 2
}

/// <summary>
/// How far a payment release has got towards the money actually being asked for.
///
/// Deliberately separate from <see cref="PaymentRequestStatus"/>: one says whether the Financial
/// Director has agreed, the other whether the email went. A request can be Approved and not yet
/// Released, or Released and Failed - and conflating them is how "it was approved" quietly comes
/// to mean "somebody clicked a button".
///
/// PendingApproval is 0 because that is where a newly raised request starts. The one request that
/// predates this workflow is set to Released by the migration rather than being left to default,
/// since it was genuinely sent.
/// </summary>
public enum PaymentReleaseStatus
{
    PendingApproval = 0,
    Approved        = 1,
    Rejected        = 2,
    Released        = 3,
    Withdrawn       = 4
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

/// <summary>
/// How VAT applies to an amount. The rate itself is never read from this enum: it is stored on
/// each invoice line, because South Africa moved from 14% to 15% in 2018 and a rate read at
/// render time would silently re-price every historical invoice the next time it changed.
///
/// Zero-rated and exempt are not the same thing and must stay distinct. Exported goods are
/// zero-rated - 0%, but still a taxable supply, and input VAT on them is still reclaimable -
/// while exempt supplies sit outside the VAT system altogether. T.M Mauritius already makes this
/// concrete: goods leaving South Africa are zero-rated while local delivery on the same job is
/// standard-rated, so one invoice can legitimately carry both.
/// </summary>
public enum VatTreatment
{
    Standard  = 0,
    ZeroRated = 1,
    Exempt    = 2
}

/// <summary>
/// Whether a record has been carried across to the external accounting system. Nothing writes
/// anything but <see cref="NotSynced"/> yet - there is no Xero integration and none is planned in
/// this work - but the column exists from the first migration so that adding one later does not
/// mean backfilling every client, invoice and receipt.
/// </summary>
public enum SyncStatus
{
    NotSynced = 0,
    Pending   = 1,
    Synced    = 2,
    Failed    = 3
}

/// <summary>
/// Where a purchase order sits on its way to becoming a commitment.
///
/// Draft is 0 deliberately, so every order that already exists lands there without a data fix -
/// the same ordering discipline <see cref="OrderType"/> and <see cref="SupplierOrigin"/> use.
///
/// This is not <see cref="FulfilmentStatus"/>. That answers "where are the goods?"; this answers
/// "has anyone agreed to buy them?". An order can be Approved and still In Production, or Issued
/// and Delivered, and the two never need to agree.
/// </summary>
public enum PoApprovalStatus
{
    Draft           = 0,
    PendingApproval = 1,
    Approved        = 2,
    Issued          = 3,
    Rejected        = 4
}

/// <summary>
/// What is being approved. One approval table serves all three rather than three near-identical
/// tables, so the Financial Director's queue is one query instead of a UNION and the rules about
/// who may decide are written once.
/// </summary>
public enum ApprovalKind
{
    PurchaseOrder   = 0,
    PaymentRelease  = 1,
    CustomerInvoice = 2
}

/// <summary>
/// The fate of one request for a decision.
///
/// Separate from <see cref="PoApprovalStatus"/>, which describes the order. An order carries one
/// current status; it may have been through several approvals to get there, and every one of them
/// is kept - a rejected request is evidence, not a draft to be overwritten.
/// </summary>
public enum ApprovalStatus
{
    Pending   = 0,
    Approved  = 1,
    Rejected  = 2,
    Withdrawn = 3
}
