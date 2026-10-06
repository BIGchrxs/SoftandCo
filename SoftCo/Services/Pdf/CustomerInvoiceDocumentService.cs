using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SoftCo.Data;
using SoftCo.Models;
using SoftCo.Services.Pdf.Models;

namespace SoftCo.Services.Pdf;

public interface ICustomerInvoiceDocumentService
{
    Task<(string FileName, byte[] Content)?> RenderAsync(int invoiceId, CancellationToken ct = default);
}

/// <summary>
/// Turns a <see cref="CustomerInvoice"/> into the document a client receives.
///
/// Where EF entities stop, as the purchase-order equivalent does. Nothing here calculates anything:
/// every figure is read from the stored line, which is why an invoice issued last year still renders
/// to the cent today even if the VAT rate has moved since.
/// </summary>
public sealed class CustomerInvoiceDocumentService : ICustomerInvoiceDocumentService
{
    private readonly AppDbContext _db;
    private readonly IPdfRenderer _renderer;
    private readonly CompanyOptions _company;

    public CustomerInvoiceDocumentService(AppDbContext db, IPdfRenderer renderer,
                                          IOptions<CompanyOptions> company)
    {
        _db = db;
        _renderer = renderer;
        _company = company.Value;
    }

    public async Task<(string FileName, byte[] Content)?> RenderAsync(int invoiceId,
                                                                      CancellationToken ct = default)
    {
        var invoice = await _db.CustomerInvoices.AsNoTracking()
            .Include(i => i.Client)
            .Include(i => i.Project)
            .Include(i => i.Lines)
            .AsSplitQuery()
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct);

        if (invoice is null) return null;

        var document = Build(invoice);
        return (FileName(invoice), _renderer.RenderCustomerInvoice(document));
    }

    private CustomerInvoiceDocument Build(CustomerInvoice invoice)
    {
        var lines = invoice.Lines
            .OrderBy(l => l.Sort)
            .Select(l => new CustomerInvoiceDocumentLine(
                l.Description,
                l.Quantity,
                l.UnitPriceExclVat,
                l.VatRatePercent,
                l.VatTreatment.Label(),
                l.LineNetExclVat,
                l.LineVatAmount))
            .ToList();

        return new CustomerInvoiceDocument(
            InvoiceNumber: invoice.InvoiceNumber ?? "DRAFT",
            IssueDate: invoice.IssueDate,
            DueDate: invoice.DueDate,
            From: Us(),
            To: Them(invoice.Client),
            Lines: lines,
            NetTotal: invoice.NetTotal,
            VatTotal: invoice.VatTotal,
            GrandTotal: invoice.GrandTotal,

            // Driven by registration, not by whether any line happens to carry VAT: a fully
            // zero-rated export issued by a registered vendor is still a tax invoice.
            IsTaxInvoice: _company.IsVatRegistered,

            ProjectName: invoice.Project?.Name,
            ClientReference: invoice.ClientReference,
            Notes: invoice.Notes,

            // Anything not yet issued is marked, so a printed draft cannot be mistaken for a tax
            // invoice - which, lacking a number, it is not.
            StatusWatermark: invoice.IsIssued ? null : $"{invoice.Status.Label()} - not a valid tax invoice");
    }

    private PdfParty Us() => new(
        Name: _company.LegalName is { Length: > 0 } legal ? legal : _company.Name,
        AddressLines: _company.LetterheadLines(),
        ContactEmail: _company.Email,
        Phone: _company.Phone,
        VatNumber: _company.IsVatRegistered ? _company.VatNumber : null,
        RegistrationNumber: _company.RegistrationNumber);

    private static PdfParty Them(Client? client)
    {
        if (client is null) return new PdfParty("(client not recorded)", []);

        return new PdfParty(
            // The registered name belongs on a tax invoice where it differs from the trading name.
            Name: client.LegalName is { Length: > 0 } legal ? legal : client.Name,
            AddressLines: client.BillingAddressLines(),
            ContactName: client.ContactName,
            ContactEmail: client.ContactEmail,
            VatNumber: client.VatNumber,
            RegistrationNumber: client.RegistrationNumber);
    }

    private static string FileName(CustomerInvoice invoice)
    {
        var reference = invoice.InvoiceNumber ?? $"draft-invoice-{invoice.Id}";

        var safe = new string(reference
            .Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-')
            .ToArray());

        return $"{safe}.pdf";
    }
}
