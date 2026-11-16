using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoftCo.Models;

namespace SoftCo.Controllers;

public class HomeController : Controller
{
    [AllowAnonymous]
    public IActionResult Index()
    {
        // The tracker is the application. There is no separate landing page to maintain.
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Orders");

        return RedirectToAction("Login", "Account");
    }

    [AllowAnonymous]
    public IActionResult Privacy() => View();

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
        => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
//_________________________...oooOOO END OF FILE OOOooo..._________________________
