using System.Security.Claims;

namespace SoftCo.Data;

/// <summary>
/// Role checks for views, reading the same constants that <c>[Authorize(Roles = ...)]</c> reads on
/// the controllers.
///
/// The views spelled the list out by hand - <c>IsInRole(Admin) || IsInRole(Procurement) ||
/// IsInRole(Finance)</c> - and it had already drifted from <see cref="Roles.CanEditOrders"/>:
/// <see cref="Roles.Staff"/>, the role every onboarded user is given, was missing from every one of
/// them. Standard users could reach /Suppliers/Create by typing the URL but were never shown the
/// button. Reading the constant means the screen and the attribute cannot disagree again.
/// </summary>
public static class RoleChecks
{
    /// <summary>May create or edit orders, suppliers, projects and clients.</summary>
    public static bool CanEditOrders(this ClaimsPrincipal user) => HasAny(user, Roles.CanEditOrders);

    /// <summary>May record or amend payments.</summary>
    public static bool CanRecordPayments(this ClaimsPrincipal user) => HasAny(user, Roles.CanRecordPayments);

    /// <summary>May see money columns anywhere in the UI.</summary>
    public static bool CanSeeValues(this ClaimsPrincipal user) => HasAny(user, Roles.CanSeeValues);

    /// <summary>May approve or reject what has been submitted.</summary>
    public static bool CanApprove(this ClaimsPrincipal user) => HasAny(user, Roles.CanApprove);

    /// <summary>May see margin and gross profit.</summary>
    public static bool CanSeeMargin(this ClaimsPrincipal user) => HasAny(user, Roles.CanSeeMargin);

    /// <summary>
    /// Splits one of the comma-separated role lists the same way the authorization filter does, and
    /// returns true when the user holds any of them. Hiding a link is presentation, never access
    /// control - the controller attribute is what actually refuses the request.
    /// </summary>
    private static bool HasAny(ClaimsPrincipal user, string roleList) =>
        roleList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(user.IsInRole);
}
