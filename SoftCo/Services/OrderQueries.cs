using Microsoft.EntityFrameworkCore;
using SoftCo.Data;
using SoftCo.Models;
using SoftCo.ViewModels;

namespace SoftCo.Services;

/// <summary>
/// Query and projection shared by the international tracker and the local orders page.
///
/// These were private members of OrdersController. They moved here when the local page was
/// added so there is exactly one definition of how an order becomes a grid row - a second copy
/// would drift, and the thing that would drift is the money.
/// </summary>
public static class OrderQueries
{
    /// <summary>Everything the grid and the detail page need loaded, for one order type.</summary>
    public static IQueryable<SupplierOrder> ForGrid(AppDbContext db, OrderType type) =>
        db.SupplierOrders
            .AsNoTracking()
            .Where(o => o.OrderType == type)
            .Include(o => o.Supplier)
            .Include(o => o.OrderProjects).ThenInclude(op => op.Project)
            .Include(o => o.Payments)
            .AsSplitQuery();

    /// <summary>Applies the filters shared by both pages. Settlement is excluded: it is computed.</summary>
    public static IQueryable<SupplierOrder> ApplyFilters(IQueryable<SupplierOrder> q, OrderFilterViewModel filter)
    {
        if (filter.SupplierId is int sid)
            q = q.Where(o => o.SupplierId == sid);

        if (filter.ProjectId is int pid)
            q = q.Where(o => o.OrderProjects.Any(op => op.ProjectId == pid));

        if (filter.Fulfilment is FulfilmentStatus fs)
            q = q.Where(o => o.FulfilmentStatus == fs);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            q = q.Where(o =>
                EF.Functions.Like(o.ProductDescription, $"%{term}%") ||
                (o.InvoiceRef != null && EF.Functions.Like(o.InvoiceRef, $"%{term}%")) ||
                (o.PoNumber != null && EF.Functions.Like(o.PoNumber, $"%{term}%")) ||
                EF.Functions.Like(o.Supplier!.Name, $"%{term}%"));
        }

        return q;
    }

    public static OrderRowViewModel ToRow(SupplierOrder o) => new()
    {
        Id = o.Id,
        PoNumber = o.PoNumber,
        OrderType = o.OrderType,
        Supplier = o.Supplier?.Name ?? "",
        Product = o.ProductDescription,
        Projects = string.Join(", ", o.OrderProjects.Select(op => op.Project?.Name).Where(n => n != null)),
        Fulfilment = o.FulfilmentStatus,
        CargoReadiness = o.CargoReadinessDate,
        InvoiceDate = o.InvoiceDate,
        InvoiceRef = o.InvoiceRef,
        CurrencyCode = o.CurrencyCode,
        InvoiceValueForeign = o.InvoiceValueForeign,
        InvoiceValueZar = o.InvoiceValueZar,
        DepositForeign = o.Payments.Where(p => p.Kind == PaymentKind.Deposit).Sum(p => p.AmountForeign),
        SettlementForeign = o.Payments.Where(p => p.Kind == PaymentKind.Settlement).Sum(p => p.AmountForeign),
        OutstandingZar = o.OutstandingZar,
        Settlement = o.SettlementStatus
    };

    public static async Task<List<(int, string)>> SupplierOptions(AppDbContext db) =>
        (await db.Suppliers.AsNoTracking().Where(s => s.IsActive)
            .OrderBy(s => s.Name).Select(s => new { s.Id, s.Name }).ToListAsync())
        .Select(s => (s.Id, s.Name)).ToList();

    public static async Task<List<(int, string)>> ProjectOptions(AppDbContext db) =>
        (await db.Projects.AsNoTracking().Where(p => p.IsActive)
            .OrderBy(p => p.Name).Select(p => new { p.Id, p.Name }).ToListAsync())
        .Select(p => (p.Id, p.Name)).ToList();
}
