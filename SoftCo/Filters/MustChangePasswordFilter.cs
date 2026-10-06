using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SoftCo.Data;

namespace SoftCo.Filters;

/// <summary>
/// Holds a user on the change-password screen until they have replaced the password an
/// administrator set for them.
///
/// Registered globally rather than attribute-by-attribute: the risk here is a controller added
/// later that nobody remembers to decorate, so the safe state is the default and the two
/// exceptions are named explicitly. Signing out is always allowed - trapping someone in a screen
/// with no way to leave is its own kind of broken.
/// </summary>
public class MustChangePasswordFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;

        if (user.Identity?.IsAuthenticated != true ||
            !user.HasClaim(AppUserClaimsPrincipalFactory.MustChangePasswordClaim, "true"))
        {
            await next();
            return;
        }

        var controller = context.RouteData.Values["controller"]?.ToString();
        var action = context.RouteData.Values["action"]?.ToString();

        var allowed =
            string.Equals(controller, "Account", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(action, "ChangePassword", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(action, "Logout", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(action, "Denied", StringComparison.OrdinalIgnoreCase));

        if (allowed)
        {
            await next();
            return;
        }

        context.Result = new RedirectToActionResult("ChangePassword", "Account", null);
    }
}
