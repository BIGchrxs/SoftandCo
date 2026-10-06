using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoftCo.Data;
using SoftCo.Models;
using SoftCo.Services;
using SoftCo.Services.Pdf;

namespace SoftCo.Controllers;

/// <summary>
/// Produces the purchase-order document for an order.
///
/// A purchase order is not an instruction to the supplier until the Financial Director has approved
/// it - that workflow arrives next. What exists here is the document itself: previewable before it
/// is committed to, and attachable to the order so there is a record of exactly what was approved.
/// </summary>
[Authorize(Roles = Roles.AnyRole)]
public class PurchaseOrdersController : Controller
{
    private readonly IPurchaseOrderDocumentService _documents;
    private readonly IAuditService _audit;
    private readonly AppDbContext _db;

    public PurchaseOrdersController(IPurchaseOrderDocumentService documents, IAuditService audit, AppDbContext db)
    {
        _documents = documents;
        _audit = audit;
        _db = db;
    }

    /// <summary>
    /// Renders the document and hands it straight back without storing it, so somebody can see what
    /// they are about to commit to. Gated on seeing values, because the whole page is money.
    /// </summary>
    [Authorize(Roles = Roles.CanSeeValues)]
    public async Task<IActionResult> Preview(int id, CancellationToken ct)
    {
        var rendered = await _documents.RenderAsync(id, ct);
        if (rendered is not var (fileName, content)) return NotFound();

        Response.Headers["X-Content-Type-Options"] = "nosniff";

        // An attachment rather than inline, matching how stored documents are served: a PDF
        // rendered in the browser from our own origin is a larger surface than one that lands in
        // the downloads folder.
        return File(content, "application/pdf", fileName);
    }

    /// <summary>
    /// Renders the document and attaches it to the order, replacing any previously generated copy.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.CanEditOrders)]
    public async Task<IActionResult> Generate(int id, CancellationToken ct)
    {
        var document = await _documents.GenerateAndStoreAsync(id, User.Identity?.Name, ct);

        if (document is null)
        {
            TempData["Flash"] = "That order no longer exists.";
            return RedirectToAction("Index", "Orders");
        }

        _audit.Record(nameof(SupplierOrder), id.ToString(), "PurchaseOrderGenerated",
                      newValue: document.OriginalFileName);
        await _db.SaveChangesAsync(ct);

        TempData["Flash"] = $"{document.OriginalFileName} generated and attached to this order.";
        return RedirectToAction("Details", "Orders", new { id });
    }
}
