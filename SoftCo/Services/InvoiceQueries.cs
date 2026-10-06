using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SoftCo.Data;
using SoftCo.Models;

namespace SoftCo.Services;

/// <summary>
/// The reads behind the invoice screens, kept out of the controller for the same reason
/// <see cref="OrderQueries"/> is: a controller that also owns its queries grows until nobody can see
/// what it filters on.
/// </summary>
public static class InvoiceQueries
{
    /// <summary>
    /// Narrows the invoice list. Every filter composes in SQL - which is only possible because
    /// <see cref="CustomerInvoice.Status"/> is stored rather than derived.
    /// </summary>
    public static IQueryable<CustomerInvoice> Filter(
        IQueryable<CustomerInvoice> q,
        CustomerInvoiceStatus? status, int? clientId, int? projectId, bool? overdueOnly, DateOnly today)
    {
        if (status is CustomerInvoiceStatus s) q = q.Where(i => i.Status == s);
        if (clientId is int c) q = q.Where(i => i.ClientId == c);
        if (projectId is int p) q = q.Where(i => i.ProjectId == p);

        if (overdueOnly == true)
            // Mirrors CustomerInvoice.IsOverdue, which cannot be translated to SQL. The two must
            // agree, so the condition is written once here and once there, side by side.
            q = q.Where(i => i.DueDate != null
                          && i.DueDate < today
                          && (i.Status == CustomerInvoiceStatus.Issued
                           || i.Status == CustomerInvoiceStatus.PartPaid));

        return q;
    }

    public static async Task<List<SelectListItem>> ClientOptions(AppDbContext db, int? keepId = null) =>
        await db.Clients.AsNoTracking()
            .Where(c => c.IsActive || c.Id == keepId)
            .OrderBy(c => c.Name)
            .Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.Name })
            .ToListAsync();

    public static async Task<List<SelectListItem>> ProjectOptions(AppDbContext db, int? clientId) =>
        await db.Projects.AsNoTracking()
            // Narrowed to the chosen client's projects where one is known: billing a client for
            // another client's project is a mistake nobody catches by reading the invoice.
            .Where(p => p.IsActive && (clientId == null || p.ClientId == clientId))
            .OrderBy(p => p.Name)
            .Select(p => new SelectListItem { Value = p.Id.ToString(), Text = p.Name })
            .ToListAsync();
}
