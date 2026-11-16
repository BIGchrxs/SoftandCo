using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoftCo.Data;
using SoftCo.Services.ExchangeRates;

namespace SoftCo.Controllers;

[ApiController]
[Route("api/exchange-rates")]
[Authorize(Roles = Roles.CanEditOrders)]
public sealed class ExchangeRatesController(ExchangeRateService rates) : ControllerBase
{
    [HttpGet("zar")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Zar([FromQuery] string? currency, CancellationToken cancellationToken)
    {
        try { return Ok(await rates.GetZarRateAsync(currency, cancellationToken)); }
        catch (ExchangeRateException error)
        {
            var status = error.Code == "unsupported_currency" ? 400
                : error.Code == "invalid_response" ? 502 : 503;
            return StatusCode(status, new { code = error.Code, message = error.Message });
        }
    }
}
//_________________________...oooOOO END OF FILE OOOooo..._________________________
