using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SoftCo.Data;
using SoftCo.Models;

namespace SoftCo.Controllers;

/// <summary>
/// The shared address book of people a payment request can be sent to. Managing it here keeps
/// addresses out of free-text fields on individual orders, where a typo becomes a payment
/// request that silently goes nowhere.
/// </summary>
[Authorize(Roles = Roles.AnyRole)]
public class PaymentContactsController : Controller
{
    private readonly AppDbContext _db;

    public PaymentContactsController(AppDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var contacts = await _db.PaymentContacts.AsNoTracking()
            .OrderByDescending(c => c.IsActive).ThenBy(c => c.Name)
            .ToListAsync();

        ViewBag.RequestCounts = await _db.PaymentRequests.AsNoTracking()
            .GroupBy(r => r.PaymentContactId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        return View(contacts);
    }

    [Authorize(Roles = Roles.CanEditOrders)]
    public IActionResult Create() => View("Edit", new PaymentContact());

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.CanEditOrders)]
    public async Task<IActionResult> Create(PaymentContact model)
    {
        model.Email = model.Email?.Trim().ToLowerInvariant() ?? "";

        if (await _db.PaymentContacts.AnyAsync(c => c.Email == model.Email))
            ModelState.AddModelError(nameof(model.Email), "That email address is already in the list.");

        if (!ModelState.IsValid) return View("Edit", model);

        model.CreatedById = User.Identity?.Name;
        _db.PaymentContacts.Add(model);
        await _db.SaveChangesAsync();

        TempData["Flash"] = $"{model.Name} added.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = Roles.CanEditOrders)]
    public async Task<IActionResult> Edit(int id)
    {
        var contact = await _db.PaymentContacts.FindAsync(id);
        return contact is null ? NotFound() : View(contact);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.CanEditOrders)]
    public async Task<IActionResult> Edit(PaymentContact model)
    {
        var contact = await _db.PaymentContacts.FindAsync(model.Id);
        if (contact is null) return NotFound();

        model.Email = model.Email?.Trim().ToLowerInvariant() ?? "";

        if (await _db.PaymentContacts.AnyAsync(c => c.Email == model.Email && c.Id != model.Id))
            ModelState.AddModelError(nameof(model.Email), "That email address is already in the list.");

        if (!ModelState.IsValid) return View(model);

        contact.Name = model.Name;
        contact.Email = model.Email;
        contact.RoleNote = model.RoleNote;

        // Deactivated, never deleted - past requests must keep pointing at a real contact row.
        contact.IsActive = model.IsActive;

        await _db.SaveChangesAsync();

        TempData["Flash"] = $"{contact.Name} updated.";
        return RedirectToAction(nameof(Index));
    }
}
