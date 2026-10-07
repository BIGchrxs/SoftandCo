using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SoftCo.Data;
using SoftCo.Models;
using SoftCo.Services.Pdf;

namespace SoftCo.Services.Reporting;

/// <summary>Gross profit for one project.</summary>
public sealed record ProjectMargin(
    int ProjectId,
    string ProjectCode,
    string ProjectName,
    string? ClientName,
    decimal Revenue,
    decimal Cost,
    decimal GrossProfit,
    decimal? MarginPercent,
    int OrderCount,
    int InvoiceCount,
    int UnknownVatOrderCount);

/// <summary>The whole report, with the caveats it has to carry.</summary>
public sealed record GrossProfitReport(
    IReadOnlyList<ProjectMargin> Projects,
    decimal TotalRevenue,
    decimal TotalCost,
    decimal TotalGrossProfit,
    decimal? TotalMarginPercent,
    int UnknownVatOrderCount,
    int UnallocatedProjectCount,
    decimal VatRatePercent);

public interface IGrossProfitService
{
    Task<GrossProfitReport> BuildAsync(CancellationToken ct = default);
}

/// <summary>
/// What Soft &amp; Co actually made on each project.
///
/// <para>Revenue is the net of issued client invoices, less issued credit notes, <b>excluding
/// VAT</b>. VAT is SARS's money passing through the business; counting it would overstate gross
/// profit by roughly 13% and the error would look like success.</para>
///
/// <para>Cost is each supplier order's value multiplied by that project's allocation share, so an
/// order serving two projects contributes to each once rather than twice. Before
/// <see cref="OrderProject.AllocationShare"/> existed there was no honest way to compute this at
/// all.</para>
///
/// <para>The report carries its own caveats rather than presenting a clean number it cannot
/// justify: how many orders have no recorded VAT treatment, and how many projects have no client.</para>
/// </summary>
public sealed class GrossProfitService : IGrossProfitService
{
    private readonly AppDbContext _db;
    private readonly CompanyOptions _company;

    public GrossProfitService(AppDbContext db, IOptions<CompanyOptions> company)
    {
        _db = db;
        _company = company.Value;
    }

    public async Task<GrossProfitReport> BuildAsync(CancellationToken ct = default)
    {
        var rate = _company.StandardVatRatePercent;

        var projects = await _db.Projects.AsNoTracking()
            .Include(p => p.Client)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

        // Orders with their project links and shares. Loaded rather than aggregated in SQL because
        // the VAT-adjusted cost depends on a per-row treatment the database cannot express.
        var orders = await _db.SupplierOrders.AsNoTracking()
            .Include(o => o.OrderProjects)
            .AsSplitQuery()
            .ToListAsync(ct);

        // Only invoices that represent real revenue. A draft is not billed; a cancelled or rejected
        // invoice never will be.
        var invoices = await _db.CustomerInvoices.AsNoTracking()
            .Include(i => i.Lines)
            .Include(i => i.CreditNotes)
            .AsSplitQuery()
            .Where(i => i.Status == CustomerInvoiceStatus.Issued
                     || i.Status == CustomerInvoiceStatus.PartPaid
                     || i.Status == CustomerInvoiceStatus.Paid)
            .ToListAsync(ct);

        var rows = new List<ProjectMargin>();

        foreach (var project in projects)
        {
            var linked = orders
                .Where(o => o.OrderProjects.Any(op => op.ProjectId == project.Id))
                .ToList();

            var cost = linked.Sum(o =>
            {
                var share = o.OrderProjects.First(op => op.ProjectId == project.Id).AllocationShare;
                return GrossProfitMath.Allocate(o.CostExclVatZar(rate), share);
            });

            var projectInvoices = invoices.Where(i => i.ProjectId == project.Id).ToList();

            // Net of VAT on both sides. Credit notes come off at their net value for the same
            // reason: the VAT on a credit note goes back to SARS, not into margin.
            var revenue = projectInvoices.Sum(i => i.Lines.Sum(l => l.LineNetExclVat))
                        - projectInvoices.Sum(i => i.CreditNotes
                            .Where(c => c.Status == CreditNoteStatus.Issued)
                            .Sum(c => c.NetTotal));

            var gp = GrossProfitMath.GrossProfit(revenue, cost);

            rows.Add(new ProjectMargin(
                project.Id, project.Code, project.Name, project.Client?.Name,
                revenue, cost, gp, GrossProfitMath.MarginPercent(revenue, gp),
                linked.Count, projectInvoices.Count,
                linked.Count(o => o.VatTreatment == SupplierVatTreatment.Unknown)));
        }

        var totalRevenue = rows.Sum(r => r.Revenue);
        var totalCost = rows.Sum(r => r.Cost);
        var totalGp = GrossProfitMath.GrossProfit(totalRevenue, totalCost);

        return new GrossProfitReport(
            rows,
            totalRevenue,
            totalCost,
            totalGp,
            GrossProfitMath.MarginPercent(totalRevenue, totalGp),

            // Counted across distinct orders, not summed per project - one order linked to three
            // projects is one order with an unrecorded treatment, not three.
            orders.Count(o => o.VatTreatment == SupplierVatTreatment.Unknown
                           && o.OrderProjects.Count > 0),

            projects.Count(p => p.ClientId is null),
            rate);
    }
}
