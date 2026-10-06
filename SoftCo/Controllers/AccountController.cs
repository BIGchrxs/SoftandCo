using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SoftCo.Data;
using SoftCo.Models;
using SoftCo.Services;
using SoftCo.ViewModels;

namespace SoftCo.Controllers;

/// <summary>
/// Signing in, signing out, and a person's own account.
///
/// The class is [Authorize] and the two genuinely public actions opt out. It used to be the other
/// way round - [AllowAnonymous] on the class with [Authorize] on individual actions - which does
/// not work: AllowAnonymous always wins no matter how far away it is declared, so those action
/// attributes were doing nothing (ASP0026). Nothing leaked, because each of those actions happens
/// to call GetUserAsync and challenge on null, but that is an accident of their implementation
/// rather than an access rule, and the next action added would not inherit the accident.
/// </summary>
[Authorize]
public class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly UserManager<ApplicationUser> _users;
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;

    public AccountController(SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users,
                             AppDbContext db, IAuditService audit)
    {
        _signIn = signIn;
        _users = users;
        _db = db;
        _audit = audit;
    }

    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl = null)
        => View(new LoginViewModel { ReturnUrl = returnUrl });

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        // lockoutOnFailure: the 5-attempt / 15-minute lockout configured in Program.cs only
        // engages if failures are actually counted here.
        var result = await _signIn.PasswordSignInAsync(vm.Email, vm.Password, isPersistent: false, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            var user = await _users.FindByEmailAsync(vm.Email);
            if (user is { IsActive: false })
            {
                await _signIn.SignOutAsync();
                ModelState.AddModelError(string.Empty, "This account has been deactivated.");
                return View(vm);
            }

            if (user is not null)
            {
                user.LastLoginAt = DateTime.UtcNow;
                await _users.UpdateAsync(user);

                _audit.Record(nameof(ApplicationUser), user.Id, "LoginSucceeded", newValue: user.Email);
                await _db.SaveChangesAsync();
            }

            // A user still holding an administrator-set password goes nowhere else first.
            if (user is { MustChangePassword: true })
                return RedirectToAction(nameof(ChangePassword));

            // Url.IsLocalUrl keeps a crafted ReturnUrl from turning the login form into an open
            // redirect that bounces a signed-in user to someone else's site.
            if (!string.IsNullOrEmpty(vm.ReturnUrl) && Url.IsLocalUrl(vm.ReturnUrl))
                return Redirect(vm.ReturnUrl);

            return RedirectToAction("Index", "Orders");
        }

        if (result.IsLockedOut)
        {
            _audit.Record(nameof(ApplicationUser), vm.Email, "LoginLockedOut");
            await _db.SaveChangesAsync();

            ModelState.AddModelError(string.Empty, "Too many failed attempts. Try again in 15 minutes.");
            return View(vm);
        }

        // The address is recorded as supplied rather than resolved to a user id: a failed attempt
        // against an address that does not exist is exactly what a spraying attack looks like, and
        // that is worth seeing in the log.
        _audit.Record(nameof(ApplicationUser), vm.Email, "LoginFailed");
        await _db.SaveChangesAsync();

        // Deliberately not "no such user" vs "wrong password" - that difference tells an
        // attacker which addresses are real.
        ModelState.AddModelError(string.Empty, "Incorrect email address or password.");
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signIn.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    public IActionResult Denied() => View();

    // --- Your account --------------------------------------------------------------------

    /// <summary>
    /// A person's own account page.
    ///
    /// [Authorize] rather than a role requirement: whatever else is true of an account, the person
    /// holding it should be able to see what it is and sign out of it. Someone still on an
    /// administrator-set password is sent to the change-password screen first by the global
    /// filter, so this is only reachable once that is done.
    /// </summary>
    public async Task<IActionResult> Profile()
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();

        return View(await BuildProfileAsync(user));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(ProfileViewModel vm)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();

        if (!ModelState.IsValid)
        {
            // Re-read the display-only fields from the database rather than trusting whatever the
            // form posted back for them.
            var reloaded = await BuildProfileAsync(user);
            vm.Email = reloaded.Email;
            vm.Roles = reloaded.Roles;
            vm.IsActive = reloaded.IsActive;
            vm.LastLoginAt = reloaded.LastLoginAt;
            vm.CreatedAt = reloaded.CreatedAt;
            return View(vm);
        }

        var firstName = string.IsNullOrWhiteSpace(vm.FirstName) ? null : vm.FirstName.Trim();
        var lastName = string.IsNullOrWhiteSpace(vm.LastName) ? null : vm.LastName.Trim();

        if (firstName == user.FirstName && lastName == user.LastName)
        {
            TempData["Flash"] = "Nothing to change.";
            return RedirectToAction(nameof(Profile));
        }

        // Only these two fields are assigned. Email is the sign-in identity and roles are an
        // administrator's decision; neither is bound from this form.
        _audit.Record(nameof(ApplicationUser), user.Id, "ProfileUpdated", "Name",
                      oldValue: $"{user.FirstName} {user.LastName}".Trim(),
                      newValue: $"{firstName} {lastName}".Trim());

        user.FirstName = firstName;
        user.LastName = lastName;

        var result = await _users.UpdateAsync(user);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(await BuildProfileAsync(user));
        }

        await _db.SaveChangesAsync();

        TempData["Flash"] = "Your details have been updated.";
        return RedirectToAction(nameof(Profile));
    }

    private async Task<ProfileViewModel> BuildProfileAsync(ApplicationUser user) => new()
    {
        FirstName = user.FirstName,
        LastName = user.LastName,
        Email = user.Email ?? "",
        Roles = (await _users.GetRolesAsync(user)).OrderBy(r => r).ToList(),
        IsActive = user.IsActive,
        LastLoginAt = user.LastLoginAt,
        CreatedAt = user.CreatedAt
    };

    // --- Change password -----------------------------------------------------------------

    public async Task<IActionResult> ChangePassword()
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();

        ViewBag.Forced = user.MustChangePassword;
        return View(new ChangePasswordViewModel());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel vm)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();

        ViewBag.Forced = user.MustChangePassword;

        if (!ModelState.IsValid) return View(vm);

        // ChangePasswordAsync verifies the current password itself, so someone who walks up to an
        // unlocked machine cannot silently take the account over.
        var result = await _users.ChangePasswordAsync(user, vm.CurrentPassword, vm.NewPassword);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(vm);
        }

        user.MustChangePassword = false;
        await _users.UpdateAsync(user);

        // Changing the password rotates the security stamp, which would otherwise sign this user
        // out mid-request. Refreshing re-issues the cookie and clears the must-change claim.
        await _signIn.RefreshSignInAsync(user);

        _audit.Record(nameof(ApplicationUser), user.Id, "PasswordChanged", newValue: user.Email);
        await _db.SaveChangesAsync();

        TempData["Flash"] = "Your password has been changed.";
        return RedirectToAction("Index", "Orders");
    }
}
