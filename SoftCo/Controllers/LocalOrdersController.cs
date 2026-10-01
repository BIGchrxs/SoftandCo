using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SoftCo.Data;
using SoftCo.Models;
using SoftCo.Services;
using SoftCo.ViewModels;

namespace SoftCo.Controllers;

/// <summary>
/// Local purchases: bought in Rand, delivered by road, never shipped or cleared.
///
/// They share the SupplierOrder table with international orders and are separated by OrderType,
/// so payments, project links, the settlement calculation and the audit trail are the same code
/// in both places. What differs is only what this controller writes and what its views show:
/// currency is fixed at ZAR with a rate of 1, and there is no cargo-readiness date.
/// </summary>
[Authorize]
public class LocalOrdersController : Controller
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly IPoNumberGenerator _poNumbers;

    public LocalOrdersController(AppDbContext db, IAuditService audit, IPoNumberGenerator poNumbers)
    {
        _db = db;
        _audit = audit;
        _poNumbers = poNumbers;
    }

    public async Task<IActionResult> Index(OrderFilterViewModel filter)
    {
        var q = OrderQueries.ApplyFilters(OrderQueries.ForGrid(_db, OrderType.Local), filter);

        var orders = await q
            .OrderByDescending(o => o.InvoiceDate ?? DateOnly.MinValue)
            .ThenByDescending(o => o.Id)
            .ToListAsync();

        var rows = orders.Select(OrderQueries.ToRow).ToList();

        if (filter.Settlement is SettlementStatus ss)
            rows = rows.Where(r => r.Settlement == ss).ToList();

        return View(new OrderListViewModel
        {
            Filter = filter,
            Rows = rows,
            Suppliers = await OrderQueries.SupplierOptions(_db),
            Projects = await OrderQueries.ProjectOptions(_db)
        });
    }

    [Authorize(Roles = Roles.CanEditOrders)]
    public async Task<IActionResult> Create() =>
        View("Edit", new OrderEditViewModel
        {
            OrderType = OrderType.Local,
            CurrencyCode = "ZAR",
            ExchangeRate = 1m,
            AllSuppliers = await OrderQueries.SupplierOptions(_db),
            AllProjects = await OrderQueries.ProjectOptions(_db)
        });

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.CanEditOrders)]
    public async Task<IActionResult> Create(OrderEditViewModel vm)
    {
        ApplyLocalDefaults(vm);

        if (!ModelState.IsValid)
        {
            vm.AllSuppliers = await OrderQueries.SupplierOptions(_db);
            vm.AllProjects = await OrderQueries.ProjectOptions(_db);
            return View("Edit", vm);
        }

        var order = new SupplierOrder
        {
            PoNumber = await _poNumbers.NextAsync(),
            OrderType = OrderType.Local,
            SupplierId = vm.SupplierId,
            ProductDescription = vm.ProductDescription,
            FulfilmentStatus = vm.FulfilmentStatus,
            InvoiceRef = vm.InvoiceRef,
            InvoiceDate = vm.InvoiceDate,
            CurrencyCode = "ZAR",
            ExchangeRate = 1m,
            InvoiceValueForeign = vm.InvoiceValueZar,
            InvoiceValueZar = vm.InvoiceValueZar,
            Notes = vm.Notes,
            CreatedById = User.Identity?.Name
        };

        foreach (var pid in vm.ProjectIds.Distinct())
            order.OrderProjects.Add(new OrderProject { ProjectId = pid });

        _db.SupplierOrders.Add(order);
        await _db.SaveChangesAsync();

        _audit.Record(nameof(SupplierOrder), order.Id.ToString(), "Created",
                      newValue: $"{order.PoNumber} local - {order.ProductDescription}");
        await _db.SaveChangesAsync();

        TempData["Flash"] = $"Local order {order.PoNumber} created.";
        return RedirectToAction("Details", "Orders", new { id = order.Id });
    }

    [Authorize(Roles = Roles.CanEditOrders)]
    public async Task<IActionResult> Edit(int id)
    {
        var order = await _db.SupplierOrders
            .Include(o => o.OrderProjects)
            .FirstOrDefaultAsync(o => o.Id == id && o.OrderType == OrderType.Local);

        if (order is null) return NotFound();

        return View(new OrderEditViewModel
        {
            Id = order.Id,
            PoNumber = order.PoNumber,
            OrderType = OrderType.Local,
            SupplierId = order.SupplierId,
            ProductDescription = order.ProductDescription,
            ProjectIds = order.OrderProjects.Select(op => op.ProjectId).ToList(),
            FulfilmentStatus = order.FulfilmentStatus,
            InvoiceRef = order.InvoiceRef,
            InvoiceDate = order.InvoiceDate,
            CurrencyCode = "ZAR",
            ExchangeRate = 1m,
            InvoiceValueZar = order.InvoiceValueZar,
            InvoiceValueForeign = order.InvoiceValueZar,
            Notes = order.Notes,
            AllSuppliers = await OrderQueries.SupplierOptions(_db),
            AllProjects = await OrderQueries.ProjectOptions(_db)
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.CanEditOrders)]
    public async Task<IActionResult> Edit(OrderEditViewModel vm)
    {
        ApplyLocalDefaults(vm);

        if (!ModelState.IsValid)
        {
            vm.AllSuppliers = await OrderQueries.SupplierOptions(_db);
            vm.AllProjects = await OrderQueries.ProjectOptions(_db);
            return View(vm);
        }

        var order = await _db.SupplierOrders
            .Include(o => o.OrderProjects)
            .FirstOrDefaultAsync(o => o.Id == vm.Id && o.OrderType == OrderType.Local);

        if (order is null) return NotFound();

        TrackChange(order.Id, "FulfilmentStatus", order.FulfilmentStatus.ToString(), vm.FulfilmentStatus.ToString());
        TrackChange(order.Id, "InvoiceValueZar", order.InvoiceValueZar.ToString(), vm.InvoiceValueZar.ToString());
        TrackChange(order.Id, "InvoiceRef", order.InvoiceRef, vm.InvoiceRef);

        order.SupplierId = vm.SupplierId;
        order.ProductDescription = vm.ProductDescription;
        order.FulfilmentStatus = vm.FulfilmentStatus;
        order.InvoiceRef = vm.InvoiceRef;
        order.InvoiceDate = vm.InvoiceDate;
        order.InvoiceValueZar = vm.InvoiceValueZar;
        order.InvoiceValueForeign = vm.InvoiceValueZar;
        order.Notes = vm.Notes;
        order.UpdatedAt = DateTime.UtcNow;
        order.UpdatedById = User.Identity?.Name;

        var wanted = vm.ProjectIds.Distinct().ToHashSet();
        foreach (var gone in order.OrderProjects.Where(op => !wanted.Contains(op.ProjectId)).ToList())
            order.OrderProjects.Remove(gone);
        foreach (var pid in wanted.Where(p => order.OrderProjects.All(op => op.ProjectId != p)))
            order.OrderProjects.Add(new OrderProject { ProjectId = pid });

        await _db.SaveChangesAsync();

        TempData["Flash"] = "Local order updated.";
        return RedirectToAction("Details", "Orders", new { id = order.Id });
    }

    /// <summary>
    /// A local order is ZAR at a rate of one, whatever the form posted. Setting this on the
    /// server rather than trusting hidden fields means a tampered post cannot create a local
    /// order with a foreign rate and quietly change what it is worth.
    /// </summary>
    private void ApplyLocalDefaults(OrderEditViewModel vm)
    {
        vm.OrderType = OrderType.Local;
        vm.CurrencyCode = "ZAR";
        vm.ExchangeRate = 1m;
        vm.InvoiceValueForeign = vm.InvoiceValueZar;
        vm.CargoReadinessDate = null;

        ModelState.Remove(nameof(vm.CurrencyCode));
        ModelState.Remove(nameof(vm.ExchangeRate));
        ModelState.Remove(nameof(vm.InvoiceValueForeign));
    }

    private void TrackChange(int orderId, string field, string? oldV, string? newV)
    {
        if (string.Equals(oldV, newV, StringComparison.Ordinal)) return;
        _audit.Record(nameof(SupplierOrder), orderId.ToString(), "Updated", field, oldV, newV);
    }
}
