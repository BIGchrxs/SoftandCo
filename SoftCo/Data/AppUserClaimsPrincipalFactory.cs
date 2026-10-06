using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SoftCo.Models;

namespace SoftCo.Data;

/// <summary>
/// Adds the "must change password" marker to the signed-in principal.
///
/// Carrying it as a claim rather than reading the user row on every request keeps the check free;
/// the claim is refreshed by RefreshSignInAsync the moment the password is actually changed, so
/// it cannot go stale in the direction that matters (a user is never left able to skip the
/// change screen because of a cached claim).
/// </summary>
public class AppUserClaimsPrincipalFactory
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>
{
    public const string MustChangePasswordClaim = "softco:must_change_password";

    public AppUserClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IOptions<IdentityOptions> options)
        : base(userManager, roleManager, options) { }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        if (user.MustChangePassword)
            identity.AddClaim(new Claim(MustChangePasswordClaim, "true"));

        return identity;
    }
}
