using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoftCo.Data;
using SoftCo.Services.Reporting;

namespace SoftCo.Controllers;

/// <summary>
/// Management reporting.
///
/// Gated on <see cref="Roles.CanSeeMargin"/>, which is deliberately narrower than
/// <see cref="Roles.CanSeeValues"/>: what an order cost and what a client was charged are both
/// visible to the people doing the work, but the difference between them is not.
/// </summary>
[Authorize(Roles = Roles.CanSeeMargin)]
public class ReportsController : Controller
{
    private readonly IGrossProfitService _grossProfit;

    public ReportsController(IGrossProfitService grossProfit) => _grossProfit = grossProfit;

    public async Task<IActionResult> GrossProfit(CancellationToken ct) =>
        View(await _grossProfit.BuildAsync(ct));
}
