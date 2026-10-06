using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SoftCo.Data;
using SoftCo.Models;
using SoftCo.ViewModels;

namespace SoftCo.Controllers;

/// <summary>
/// The register of people Soft &amp; Co invoice. Modelled on <see cref="SuppliersController"/> -
/// the two are mirror images, one register for money out and one for money in - so the screens
/// behave the same way and there is only one pattern to learn.
///
/// There is no Delete. A client is deactivated instead: invoices and projects reference it with
/// <c>Restrict</c>, and a client who has been billed is part of the financial record.
/// </summary>
[Authorize(Roles = Roles.AnyRole)]
public class ClientsController : Controller
{
    private readonly AppDbContext _db;

    public ClientsController(AppDbContext db) => _db = db;

    public async Task<IActionResult> Index(bool? active)
    {
        var q = _db.Clients.AsNoTracking().AsQueryable();
        if (active is bool a) q = q.Where(c => c.IsActive == a);

        ViewBag.Active = active;

        // Projects per client, so the register shows which clients actually have work on.
        ViewBag.ProjectCounts = await _db.Projects.AsNoTracking()
            .Where(p => p.ClientId != null)
            .GroupBy(p => p.ClientId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        // The backlog from not backfilling the project register. Shown here as well as on the
        // projects page, because this is where someone goes to create the client it needs.
        ViewBag.UnassignedProjects = await _db.Projects.CountAsync(p => p.ClientId == null);

        return View(await q.OrderByDescending(c => c.IsActive).ThenBy(c => c.Name).ToListAsync());
    }

    [Authorize(Roles = Roles.CanEditOrders)]
    public IActionResult Create() => View("Edit", new ClientEditViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.CanEditOrders)]
    public async Task<IActionResult> Create(ClientEditViewModel vm)
    {
        await CheckUniqueAsync(vm);
        if (!ModelState.IsValid) return View("Edit", vm);

        var client = new Client { CreatedById = User.Identity?.Name };
        vm.ApplyTo(client);

        _db.Clients.Add(client);
        await _db.SaveChangesAsync();

        TempData["Flash"] = $"{client.Name} added.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = Roles.CanEditOrders)]
    public async Task<IActionResult> Edit(int id)
    {
        var client = await _db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (client is null) return NotFound();

        var projectCount = await _db.Projects.CountAsync(p => p.ClientId == id);
        return View(ClientEditViewModel.FromEntity(client, projectCount));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.CanEditOrders)]
    public async Task<IActionResult> Edit(ClientEditViewModel vm)
    {
        var client = await _db.Clients.FindAsync(vm.Id);
        if (client is null) return NotFound();

        await CheckUniqueAsync(vm);

        if (!ModelState.IsValid)
        {
            // Re-read the fields the form shows but does not own, so a validation failure does not
            // blank them on the way back.
            vm.SyncStatus = client.SyncStatus;
            vm.ProjectCount = await _db.Projects.CountAsync(p => p.ClientId == vm.Id);
            return View(vm);
        }

        vm.ApplyTo(client);
        await _db.SaveChangesAsync();

        TempData["Flash"] = $"{client.Name} updated.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Catches a duplicate code or name before the insert, so the user gets a message against the
    /// field rather than a 500 from the unique index. Case-insensitive on the name: "Williams" and
    /// "williams" are the same client, and the index alone would not stop the second.
    /// </summary>
    private async Task CheckUniqueAsync(ClientEditViewModel vm)
    {
        var code = (vm.Code ?? "").Trim().ToUpperInvariant();
        var name = (vm.Name ?? "").Trim().ToLowerInvariant();

        if (code.Length > 0 &&
            await _db.Clients.AnyAsync(c => c.Code.ToUpper() == code && c.Id != vm.Id))
            ModelState.AddModelError(nameof(vm.Code), "Another client already uses that code.");

        if (name.Length > 0 &&
            await _db.Clients.AnyAsync(c => c.Name.ToLower() == name && c.Id != vm.Id))
            ModelState.AddModelError(nameof(vm.Name), "A client with that name is already in the register.");
    }
}
