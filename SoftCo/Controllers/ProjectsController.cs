using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SoftCo.Data;
using SoftCo.Models;
using SoftCo.Services.Reporting;
using SoftCo.ViewModels;

namespace SoftCo.Controllers;

[Authorize(Roles = Roles.AnyRole)]
public class ProjectsController : Controller
{
    private readonly AppDbContext _db;

    public ProjectsController(AppDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var projects = await _db.Projects.AsNoTracking()
            .Include(p => p.Client)
            .OrderBy(p => p.Name)
            .ToListAsync();

        // Exposure per project, in Rand. Foreign columns mix CNY and USD and cannot be summed.
        var orders = await _db.SupplierOrders.AsNoTracking()
            .Include(o => o.OrderProjects)
            .Include(o => o.Payments)
            .AsSplitQuery()
            .ToListAsync();

        // Allocated by share, not counted in full against every linked project. Until
        // OrderProject.AllocationShare existed this summed an order's whole balance into each of
        // its projects - which is why the note at the foot of this page had to warn that the
        // figures were not additive. They are now.
        ViewBag.Exposure = projects.ToDictionary(
            p => p.Id,
            p => orders.SelectMany(o => o.OrderProjects
                                         .Where(op => op.ProjectId == p.Id)
                                         .Select(op => GrossProfitMath.Allocate(o.OutstandingZar,
                                                                                op.AllocationShare)))
                       .Sum());

        ViewBag.Counts = projects.ToDictionary(
            p => p.Id,
            p => orders.Count(o => o.OrderProjects.Any(op => op.ProjectId == p.Id)));

        return View(projects);
    }

    [Authorize(Roles = Roles.CanEditOrders)]
    public async Task<IActionResult> Create() =>
        View("Edit", new ProjectEditViewModel { Clients = await ClientOptionsAsync() });

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.CanEditOrders)]
    public async Task<IActionResult> Create(ProjectEditViewModel vm)
    {
        await ValidateAsync(vm);

        if (!ModelState.IsValid)
        {
            vm.Clients = await ClientOptionsAsync();
            return View("Edit", vm);
        }

        _db.Projects.Add(new Project
        {
            Code = Code(vm),
            Name = vm.Name.Trim(),
            ClientId = vm.ClientId,
            IsActive = vm.IsActive
        });
        await _db.SaveChangesAsync();

        TempData["Flash"] = "Project added.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = Roles.CanEditOrders)]
    public async Task<IActionResult> Edit(int id)
    {
        var p = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (p is null) return NotFound();

        return View(new ProjectEditViewModel
        {
            Id = p.Id,
            Code = p.Code,
            Name = p.Name,
            ClientId = p.ClientId,
            IsActive = p.IsActive,
            Clients = await ClientOptionsAsync(p.ClientId)
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.CanEditOrders)]
    public async Task<IActionResult> Edit(ProjectEditViewModel vm)
    {
        var p = await _db.Projects.FindAsync(vm.Id);
        if (p is null) return NotFound();

        await ValidateAsync(vm);

        if (!ModelState.IsValid)
        {
            vm.Clients = await ClientOptionsAsync(p.ClientId);
            return View(vm);
        }

        p.Code = Code(vm);
        p.Name = vm.Name.Trim();
        p.ClientId = vm.ClientId;
        p.IsActive = vm.IsActive;

        await _db.SaveChangesAsync();

        TempData["Flash"] = "Project updated.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Every project code in the register is already upper case, so normalising here matches the
    /// existing data rather than reformatting it.
    /// </summary>
    private static string Code(ProjectEditViewModel vm) => vm.Code.Trim().ToUpperInvariant();

    private async Task<List<ClientOption>> ClientOptionsAsync(int? keepId = null)
    {
        // Inactive clients are left out of the list, except the one this project already points at:
        // dropping it silently would reassign the project the next time someone saved an unrelated
        // field.
        return await _db.Clients.AsNoTracking()
            .Where(c => c.IsActive || c.Id == keepId)
            .OrderBy(c => c.Name)
            .Select(c => new ClientOption(c.Id, c.Code, c.Name))
            .ToListAsync();
    }

    /// <summary>
    /// The checks that need the database. The view model owns the rest, including the rule that a
    /// new project must name a client.
    /// </summary>
    private async Task ValidateAsync(ProjectEditViewModel vm)
    {
        var code = Code(vm);
        if (code.Length > 0 && await _db.Projects.AnyAsync(p => p.Code == code && p.Id != vm.Id))
            ModelState.AddModelError(nameof(vm.Code), "Another project already uses that code.");

        if (vm.ClientId is not int clientId) return;

        // The dropdown offers only valid choices, but the id arrives in a form post and nothing
        // stops a different one being sent. The foreign key would reject an unknown client with a
        // 500; this gives the field an error instead, and catches the inactive case the key cannot.
        var client = await _db.Clients.AsNoTracking()
            .Where(c => c.Id == clientId)
            .Select(c => new { c.IsActive })
            .FirstOrDefaultAsync();

        if (client is null)
            ModelState.AddModelError(nameof(vm.ClientId), "That client is not in the register.");
        else if (!client.IsActive && vm.IsNew)
            ModelState.AddModelError(nameof(vm.ClientId),
                "That client is inactive. Reactivate them before starting new work.");
    }
}
