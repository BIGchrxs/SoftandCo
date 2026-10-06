using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SoftCo.Data;
using SoftCo.Models;
using SoftCo.Services.Documents;
using SoftCo.Services.Pdf.Models;

namespace SoftCo.Services.Pdf;

public interface IPurchaseOrderDocumentService
{
    /// <summary>Builds the document model for an order, or null if the order does not exist.</summary>
    Task<PurchaseOrderDocument?> BuildAsync(int orderId, CancellationToken ct = default);

    /// <summary>Renders the purchase order and returns the bytes without storing anything.</summary>
    Task<(string FileName, byte[] Content)?> RenderAsync(int orderId, CancellationToken ct = default);

    /// <summary>
    /// Renders the purchase order and attaches it to the order, replacing any previously generated
    /// copy so there is never more than one current PO document per order.
    /// </summary>
    Task<OrderDocument?> GenerateAndStoreAsync(int orderId, string? userId, CancellationToken ct = default);
}

/// <summary>
/// Turns a <see cref="SupplierOrder"/> into the purchase order the Financial Director approves.
///
/// This is where EF entities stop. It reads the order, flattens it into the plain records in
/// <c>Services.Pdf.Models</c>, and hands those to <see cref="IPdfRenderer"/> - so the renderer never
/// sees a DbContext and the document model can be asserted on without a database.
/// </summary>
public sealed class PurchaseOrderDocumentService : IPurchaseOrderDocumentService
{
    private readonly AppDbContext _db;
    private readonly IPdfRenderer _renderer;
    private readonly IDocumentStore _store;
    private readonly CompanyOptions _company;
    private readonly TimeProvider _clock;

    public PurchaseOrderDocumentService(AppDbContext db, IPdfRenderer renderer, IDocumentStore store,
                                        IOptions<CompanyOptions> company, TimeProvider clock)
    {
        _db = db;
        _renderer = renderer;
        _store = store;
        _company = company.Value;
        _clock = clock;
    }

    public async Task<PurchaseOrderDocument?> BuildAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _db.SupplierOrders.AsNoTracking()
            .Include(o => o.Supplier)
            .Include(o => o.OrderProjects).ThenInclude(op => op.Project)
            .AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);

        return order is null ? null : Build(order);
    }

    public async Task<(string FileName, byte[] Content)?> RenderAsync(int orderId, CancellationToken ct = default)
    {
        var document = await BuildAsync(orderId, ct);
        if (document is null) return null;

        return (FileName(document), _renderer.RenderPurchaseOrder(document));
    }

    public async Task<OrderDocument?> GenerateAndStoreAsync(int orderId, string? userId,
                                                            CancellationToken ct = default)
    {
        var rendered = await RenderAsync(orderId, ct);
        if (rendered is null) return null;

        var (fileName, content) = rendered.Value;

        // Regenerating replaces rather than accumulates. A purchase order is the current statement
        // of what is being committed to; a folder holding four of them, three stale, is how the
        // wrong one ends up attached to an approval email. The bytes of the old one are left on
        // disk deliberately - removing a file is not something a failed transaction can undo.
        var previous = await _db.OrderDocuments
            .Where(d => d.SupplierOrderId == orderId && d.Kind == DocumentKind.PurchaseOrder)
            .ToListAsync(ct);

        if (previous.Count > 0) _db.OrderDocuments.RemoveRange(previous);

        var document = await _store.SaveGeneratedAsync(
            orderId, content, fileName, DocumentKind.PurchaseOrder, userId, ct);

        _db.OrderDocuments.Add(document);
        await _db.SaveChangesAsync(ct);

        return document;
    }

    /// <summary>
    /// The mapping, kept separate and static so it can be exercised directly against a
    /// hand-built order with no database in sight.
    /// </summary>
    internal PurchaseOrderDocument Build(SupplierOrder order)
    {
        var projects = string.Join(", ", order.OrderProjects
            .Select(op => op.Project?.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .OrderBy(n => n));

        var line = new PurchaseOrderLine(
            Description: order.ProductDescription,
            Projects: projects.Length > 0 ? projects : null,
            CurrencyCode: order.CurrencyCode,
            AmountForeign: order.InvoiceValueForeign,
            ExchangeRate: order.ExchangeRate,
            AmountZar: order.InvoiceValueZar);

        return new PurchaseOrderDocument(
            // An order saved before numbering existed, or mid-creation, has no reference yet. The
            // document says so rather than printing an empty box where the number belongs.
            PoNumber: order.PoNumber ?? "(not yet numbered)",
            IssuedOn: DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime),
            From: Us(),
            To: Them(order.Supplier),
            Lines: [line],
            PreparedBy: order.CreatedById,
            CargoReadinessDate: order.CargoReadinessDate,
            SupplierInvoiceRef: order.InvoiceRef,
            Notes: order.Notes);
    }

    private PdfParty Us() => new(
        Name: _company.Name,
        AddressLines: _company.LetterheadLines(),
        ContactEmail: _company.Email,
        Phone: _company.Phone,
        VatNumber: _company.IsVatRegistered ? _company.VatNumber : null,
        RegistrationNumber: _company.RegistrationNumber);

    private static PdfParty Them(Supplier? supplier)
    {
        if (supplier is null) return new PdfParty("(supplier not recorded)", []);

        return new PdfParty(
            Name: supplier.Name,
            AddressLines: string.IsNullOrWhiteSpace(supplier.Country) ? [] : [supplier.Country],
            ContactName: supplier.ContactName,
            ContactEmail: supplier.ContactEmail);
    }

    /// <summary>
    /// Named after the reference, so a PDF sitting in somebody's downloads folder still says what
    /// it is. Non-filename characters are stripped because the PO number reaches this as text.
    /// </summary>
    private static string FileName(PurchaseOrderDocument document)
    {
        var safe = new string(document.PoNumber
            .Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-')
            .ToArray());

        return $"{safe}.pdf";
    }
}
