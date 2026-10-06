using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SoftCo.Data;
using SoftCo.Models;

namespace SoftCo.Controllers;

/// <summary>
/// Manufacturers and freight/clearing agents share one register, separated by Type - the
/// tracker's "Suppliers" and "Service Provider" sheets carry identical columns.
/// </summary>
[Authorize(Roles = Roles.AnyRole)]
public class SuppliersController : Controller
{
    private readonly AppDbContext _db;

    public SuppliersController(AppDbContext db) => _db = db;

    /// <summary>
    /// Two independent filters. Type answers "what do they do?" (manufacturer or freight agent);
    /// Origin answers "where are they?" (local or international). They compose rather than
    /// replace each other, so a view can be narrowed to local freight agents.
    /// </summary>
    public async Task<IActionResult> Index(SupplierType? type, SupplierOrigin? origin)
    {
        var q = _db.Suppliers.AsNoTracking().AsQueryable();
        if (type is SupplierType t) q = q.Where(s => s.Type == t);
        if (origin is SupplierOrigin o) q = q.Where(s => s.Origin == o);

        ViewBag.Type = type;
        ViewBag.Origin = origin;
        ViewBag.OrderCounts = await _db.SupplierOrders.AsNoTracking()
            .GroupBy(o => o.SupplierId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        return View(await q.OrderBy(s => s.Name).ToListAsync());
    }

    [Authorize(Roles = Roles.CanEditOrders)]
    public IActionResult Create() => View("Edit", new Supplier());

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.CanEditOrders)]
    public async Task<IActionResult> Create(Supplier model)
    {
        if (!ModelState.IsValid) return View("Edit", model);

        _db.Suppliers.Add(model);
        await _db.SaveChangesAsync();

        TempData["Flash"] = "Supplier added.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = Roles.CanEditOrders)]
    public async Task<IActionResult> Edit(int id)
    {
        var s = await _db.Suppliers.FindAsync(id);
        return s is null ? NotFound() : View(s);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.CanEditOrders)]
    public async Task<IActionResult> Edit(Supplier model)
    {
        if (!ModelState.IsValid) return View(model);

        var s = await _db.Suppliers.FindAsync(model.Id);
        if (s is null) return NotFound();

        s.Name = model.Name;
        s.Type = model.Type;
        s.Origin = model.Origin;
        s.Country = model.Country;
        s.DefaultCurrencyCode = model.DefaultCurrencyCode.ToUpperInvariant();
        s.ContactName = model.ContactName;
        s.ContactEmail = model.ContactEmail;
        s.IsActive = model.IsActive;

        await _db.SaveChangesAsync();

        TempData["Flash"] = "Supplier updated.";
        return RedirectToAction(nameof(Index));
    }
}
