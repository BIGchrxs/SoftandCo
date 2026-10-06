namespace SoftCo.Services.Pdf.Models;

/// <summary>
/// One side of a document's header - Soft &amp; Co at the top, the supplier opposite.
///
/// Plain strings rather than a <c>Supplier</c> or <c>Client</c>: a document is a snapshot of what
/// was true when it was issued, and an entity would re-render last year's purchase order with this
/// year's address. Nothing in this namespace references EF or ASP.NET, so the whole document model
/// can be built and asserted on in a unit test.
/// </summary>
public sealed record PdfParty(
    string Name,
    IReadOnlyList<string> AddressLines,
    string? ContactName = null,
    string? ContactEmail = null,
    string? Phone = null,
    string? VatNumber = null,
    string? RegistrationNumber = null);

/// <summary>
/// A line of the order. Today a supplier order carries exactly one, so every purchase order has a
/// single line; it is a list because that is the shape of the document, and multi-line orders are
/// a data change rather than a rewrite of the renderer.
/// </summary>
public sealed record PurchaseOrderLine(
    string Description,
    string? Projects,
    string CurrencyCode,
    decimal AmountForeign,
    decimal ExchangeRate,
    decimal AmountZar);

/// <summary>
/// Everything that prints on a purchase order, already resolved to text and numbers.
///
/// This goes to the Financial Director for approval, not to the supplier. That is why it carries
/// <see cref="PreparedBy"/> and a Rand value for every foreign amount: the person approving it is
/// deciding whether Soft &amp; Co should commit this much money, and they think in Rand.
/// </summary>
public sealed record PurchaseOrderDocument(
    string PoNumber,
    DateOnly IssuedOn,
    PdfParty From,
    PdfParty To,
    IReadOnlyList<PurchaseOrderLine> Lines,
    string? PreparedBy = null,
    DateOnly? CargoReadinessDate = null,
    string? SupplierInvoiceRef = null,
    string? Notes = null)
{
    /// <summary>
    /// The Rand total the approver is being asked to commit to. Summed from the lines rather than
    /// passed in, so the figure on the page can never disagree with the rows above it.
    /// </summary>
    public decimal TotalZar => Lines.Sum(l => l.AmountZar);

    /// <summary>
    /// The foreign total, but only when every line shares one currency. Mixed currencies cannot be
    /// added, so this reports null rather than a meaningless sum - the same rule the project
    /// exposure figures already follow.
    /// </summary>
    public (string Currency, decimal Amount)? TotalForeign
    {
        get
        {
            if (Lines.Count == 0) return null;

            var currency = Lines[0].CurrencyCode;
            return Lines.All(l => l.CurrencyCode == currency)
                ? (currency, Lines.Sum(l => l.AmountForeign))
                : null;
        }
    }
}
