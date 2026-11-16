using Microsoft.AspNetCore.Identity;
using SoftCo.Models;

namespace SoftCo.Data;

/// <summary>
/// Creates the role set and the single bootstrap Admin account on first run. There are no demo
/// users and no hard-coded passwords: the bootstrap password comes from configuration
/// (Seed:AdminPassword, supplied as Seed__AdminPassword by Docker / the ECS task definition), and
/// the account is only ever created when no users exist at all.
/// </summary>
public static class SeedData
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration config, IHostEnvironment env)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        // Only bootstrap when the instance has no users. This is what stops a redeploy from
        // resurrecting or resetting an account that an administrator has since changed.
        if (userManager.Users.Any())
            return;

        var email = config["Seed:AdminEmail"] ?? "admin@softandco.co.za";
        var password = config["Seed:AdminPassword"];

        if (string.IsNullOrWhiteSpace(password))
        {
            if (!env.IsDevelopment())
                throw new InvalidOperationException(
                    "Seed:AdminPassword must be configured (as Seed__AdminPassword) so the first " +
                    "administrator account can be created. Refusing to seed a known password.");

            // Development only, and still not a password that exists anywhere in source control
            // beyond this line: change it immediately after the first local login.
            password = "ChangeMe!" + Guid.NewGuid().ToString("N")[..8];
            Console.WriteLine($"[seed] Development bootstrap admin: {email} / {password}");
        }

        var admin = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = "System",
            LastName = "Administrator",
            IsActive = true
        };

        var result = await userManager.CreateAsync(admin, password);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                "Failed to create the bootstrap admin: " +
                string.Join("; ", result.Errors.Select(e => e.Description)));

        await userManager.AddToRoleAsync(admin, Roles.Admin);
    }
}
//_________________________...oooOOO END OF FILE OOOooo..._________________________
