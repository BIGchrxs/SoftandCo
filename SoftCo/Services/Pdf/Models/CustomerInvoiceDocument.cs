namespace SoftCo.Services.Pdf.Models;

/// <summary>
/// One line of a client invoice, already priced.
///
/// <see cref="VatRatePercent"/> travels with the line rather than being passed once for the
/// document, because one invoice can carry several rates - zero-rated exports beside standard-rated
/// local delivery - and because an invoice issued at 14% must re-render at 14% forever.
/// </summary>
public sealed record CustomerInvoiceDocumentLine(
    string Description,
    decimal Quantity,
    decimal UnitPriceExclVat,
    decimal VatRatePercent,
    string VatTreatmentLabel,
    decimal LineNetExclVat,
    decimal LineVatAmount);

/// <summary>
/// Everything that prints on a client invoice.
///
/// <see cref="IsTaxInvoice"/> decides whether the document calls itself a TAX INVOICE and shows
/// Soft &amp; Co's VAT number. It comes from configuration rather than from whether any line happens
/// to carry VAT: a fully zero-rated export issued by a registered vendor is still a tax invoice, and
/// claiming to be one while not registered is a different problem entirely.
/// </summary>
public sealed record CustomerInvoiceDocument(
    string InvoiceNumber,
    DateOnly? IssueDate,
    DateOnly? DueDate,
    PdfParty From,
    PdfParty To,
    IReadOnlyList<CustomerInvoiceDocumentLine> Lines,
    decimal NetTotal,
    decimal VatTotal,
    decimal GrandTotal,
    bool IsTaxInvoice,
    string? ProjectName = null,
    string? ClientReference = null,
    string? Notes = null,
    string? StatusWatermark = null)
{
    /// <summary>
    /// The VAT actually charged, broken down by rate. A client checking an invoice adds up the
    /// standard-rated lines and expects the VAT figure to match; showing one total against a mixed
    /// invoice invites exactly that query.
    /// </summary>
    public IReadOnlyList<(decimal Rate, decimal Net, decimal Vat)> VatBreakdown() =>
        Lines.GroupBy(l => l.VatRatePercent)
             .OrderByDescending(g => g.Key)
             .Select(g => (g.Key, g.Sum(l => l.LineNetExclVat), g.Sum(l => l.LineVatAmount)))
             .ToList();

    /// <summary>True when more than one rate applies, which is when the breakdown earns its space.</summary>
    public bool HasMixedRates => Lines.Select(l => l.VatRatePercent).Distinct().Count() > 1;
}
