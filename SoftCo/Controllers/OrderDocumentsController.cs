using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SoftCo.Data;
using SoftCo.Models;
using SoftCo.Services;
using SoftCo.Services.Documents;

namespace SoftCo.Controllers;

/// <summary>
/// Serves order attachments. The files live under App_Data, outside wwwroot, so this action is
/// the only route to them and every request passes through authentication first.
/// </summary>
[Authorize]
public class OrderDocumentsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IDocumentStore _store;
    private readonly IAuditService _audit;

    public OrderDocumentsController(AppDbContext db, IDocumentStore store, IAuditService audit)
    {
        _db = db;
        _store = store;
        _audit = audit;
    }

    public async Task<IActionResult> Download(int id)
    {
        var doc = await _db.OrderDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
        if (doc is null) return NotFound();

        if (!_store.Exists(doc))
            return NotFound();

        // Always an attachment, never inline: a PDF rendered in the browser from our own origin
        // is a larger surface than one that lands in the downloads folder.
        Response.Headers["X-Content-Type-Options"] = "nosniff";

        return File(_store.OpenRead(doc), doc.ContentType, doc.OriginalFileName);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.CanEditOrders)]
    public async Task<IActionResult> Delete(int id)
    {
        var doc = await _db.OrderDocuments.FirstOrDefaultAsync(d => d.Id == id);
        if (doc is null) return NotFound();

        var orderId = doc.SupplierOrderId;

        _db.OrderDocuments.Remove(doc);

        _audit.Record(nameof(SupplierOrder), orderId.ToString(), "DocumentRemoved",
                      field: doc.Kind.ToString(), oldValue: doc.OriginalFileName);

        await _db.SaveChangesAsync();

        // The row is the record; the file on disk is left in place deliberately so a mistaken
        // click cannot destroy the only copy of a supplier invoice. Cleaning orphaned files is
        // an administrative job, not a user action.
        TempData["Flash"] = $"{doc.OriginalFileName} removed from the order.";
        return RedirectToAction("Details", "Orders", new { id = orderId });
    }
}
