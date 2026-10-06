using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SoftCo.Data;
using SoftCo.Models;
using SoftCo.Services;
using SoftCo.ViewModels;

namespace SoftCo.Controllers;

/// <summary>
/// Onboarding and managing staff accounts.
///
/// The role requirement sits on the class, so every action - including any added later - is
/// closed by default. Guessing the URL is not a way in; vertical privilege escalation by
/// browsing to an admin path is the single most common access-control failure there is.
/// </summary>
[Authorize(Roles = Roles.Admin)]
public class UsersController : Controller
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;

    public UsersController(UserManager<ApplicationUser> users, AppDbContext db, IAuditService audit)
    {
        _users = users;
        _db = db;
        _audit = audit;
    }

    public async Task<IActionResult> Index()
    {
        var users = await _db.Users.AsNoTracking().OrderBy(u => u.Email).ToListAsync();

        var rows = new List<UserRowViewModel>();
        foreach (var u in users)
        {
            rows.Add(new UserRowViewModel
            {
                Id = u.Id,
                Email = u.Email ?? "",
                DisplayName = u.DisplayName,
                IsActive = u.IsActive,
                MustChangePassword = u.MustChangePassword,
                LastLoginAt = u.LastLoginAt,
                CreatedAt = u.CreatedAt,
                Roles = (await _users.GetRolesAsync(u)).OrderBy(r => r).ToList(),
                IsSelf = u.Id == _users.GetUserId(User)
            });
        }

        return View(rows);
    }

    public IActionResult Create() => View(new CreateUserViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateUserViewModel vm)
    {
        // An allow-list, never the posted string. Without this, editing the form to say "Admin"
        // would be a working privilege escalation on the one screen that mints accounts.
        if (!Roles.Assignable.Contains(vm.Role, StringComparer.Ordinal))
            ModelState.AddModelError(nameof(vm.Role), "Choose an access level from the list.");

        if (!ModelState.IsValid) return View(vm);

        var email = vm.Email.Trim().ToLowerInvariant();

        if (await _users.FindByEmailAsync(email) is not null)
        {
            // An administrator is entitled to a clear answer here; the vague wording used on the
            // public login form exists to stop strangers enumerating accounts, which does not
            // apply to someone who can already list every user on the previous screen.
            // Audited here as well as in AddErrors: most duplicate attempts are stopped by this
            // pre-check and never reach CreateAsync, so auditing only the race would record almost
            // none of them.
            _audit.Record(nameof(ApplicationUser), email, "UserCreateFailed", newValue: "duplicate email");
            await _db.SaveChangesAsync();

            ModelState.AddModelError(nameof(vm.Email), "An account with that email already exists.");
            return View(vm);
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = vm.FirstName?.Trim(),
            LastName = vm.LastName?.Trim(),
            IsActive = true,
            // The administrator typed this password, so it is shared knowledge from the moment it
            // exists. It gets the user in once and then has to be replaced.
            MustChangePassword = true
        };

        // CreateAsync applies the configured password policy and hashes with PBKDF2. The plaintext
        // never leaves this method and is never logged.
        var result = await _users.CreateAsync(user, vm.Password);

        if (!result.Succeeded)
        {
            AddErrors(result, email);
            return View(vm);
        }

        // The role is what the application actually authorises against, so a user that exists
        // without one is a half-made account. If this fails the user row is removed again rather
        // than left behind - every working controller now requires a role, but an account that can
        // sign in and reach nothing is still a support call nobody can explain.
        var roleResult = await _users.AddToRoleAsync(user, vm.Role);

        if (!roleResult.Succeeded)
        {
            await _users.DeleteAsync(user);

            _audit.Record(nameof(ApplicationUser), email, "UserCreateFailed",
                          newValue: "role assignment failed");
            await _db.SaveChangesAsync();

            ModelState.AddModelError(string.Empty,
                "The account could not be given its access level, so it was not created. Try again.");
            return View(vm);
        }

        _audit.Record(nameof(ApplicationUser), user.Id, "UserCreated",
                      newValue: $"{email} ({vm.Role})");
        await _db.SaveChangesAsync();

        TempData["Flash"] = $"{email} can now sign in. They will be asked to set their own password.";
        return RedirectToAction(nameof(Index));
    }


    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(string id, bool active)
    {
        var user = await _users.FindByIdAsync(id);
        if (user is null) return NotFound();

        // Without this an administrator can switch off the only account that can switch it back on.
        if (user.Id == _users.GetUserId(User))
        {
            TempData["Flash"] = "You cannot deactivate your own account.";
            return RedirectToAction(nameof(Index));
        }

        user.IsActive = active;
        await _users.UpdateAsync(user);

        // Deactivation has to reach sessions that are already open, not just future sign-ins.
        // Rotating the security stamp invalidates the cookie they are holding.
        if (!active) await _users.UpdateSecurityStampAsync(user);

        _audit.Record(nameof(ApplicationUser), user.Id,
                      active ? "UserActivated" : "UserDeactivated",
                      oldValue: (!active).ToString(), newValue: active.ToString());
        await _db.SaveChangesAsync();

        TempData["Flash"] = $"{user.Email} {(active ? "reactivated" : "deactivated")}.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Changes what an account may do. Replaces rather than adds: Staff and Financial Director are
    /// exclusive on purpose, because an account holding both could approve its own submissions.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetRole(string id, string role)
    {
        if (!Roles.Assignable.Contains(role, StringComparer.Ordinal))
        {
            TempData["Flash"] = "That is not an access level this screen can assign.";
            return RedirectToAction(nameof(Index));
        }

        var user = await _users.FindByIdAsync(id);
        if (user is null) return NotFound();

        // An administrator changing their own role could remove the last account able to change it
        // back, and the Users screen is the only way in.
        if (user.Id == _users.GetUserId(User))
        {
            TempData["Flash"] = "You cannot change your own access level.";
            return RedirectToAction(nameof(Index));
        }

        var current = await _users.GetRolesAsync(user);

        if (current.Contains(Roles.Admin))
        {
            TempData["Flash"] = "Administrator accounts are not changed from this screen.";
            return RedirectToAction(nameof(Index));
        }

        if (current.Count > 0) await _users.RemoveFromRolesAsync(user, current);
        var result = await _users.AddToRoleAsync(user, role);

        if (!result.Succeeded)
        {
            AddErrors(result, user.Email, "UserRoleChangeFailed");
            await _db.SaveChangesAsync();
            TempData["Flash"] = "That access level could not be applied.";
            return RedirectToAction(nameof(Index));
        }

        // The role lives in the sign-in cookie, so a change has to reach sessions already open or
        // the user keeps the access they had until they next sign in.
        await _users.UpdateSecurityStampAsync(user);

        _audit.Record(nameof(ApplicationUser), user.Id, "UserRoleChanged",
                      field: "Role", oldValue: string.Join(", ", current), newValue: role);
        await _db.SaveChangesAsync();

        TempData["Flash"] = $"{user.Email} is now {Roles.Label(role)}. They will need to sign in again.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> ResetPassword(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user is null) return NotFound();

        return View(new ResetUserPasswordViewModel { UserId = user.Id, Email = user.Email ?? "" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetUserPasswordViewModel vm)
    {
        var user = await _users.FindByIdAsync(vm.UserId);
        if (user is null) return NotFound();

        vm.Email = user.Email ?? "";

        if (!ModelState.IsValid) return View(vm);

        // Generate-then-redeem rather than writing a hash directly: this is the supported path,
        // it applies the password policy, and it rotates the security stamp so any session opened
        // with the old password stops working.
        var token = await _users.GeneratePasswordResetTokenAsync(user);
        var result = await _users.ResetPasswordAsync(user, token, vm.Password);

        if (!result.Succeeded)
        {
            AddErrors(result, user.Email, "PasswordResetFailed");
            return View(vm);
        }

        user.MustChangePassword = true;
        await _users.UpdateAsync(user);

        // Someone locked out by failed attempts is usually the reason a reset was asked for.
        await _users.ResetAccessFailedCountAsync(user);
        await _users.SetLockoutEndDateAsync(user, null);

        _audit.Record(nameof(ApplicationUser), user.Id, "PasswordResetByAdmin",
                      newValue: user.Email);
        await _db.SaveChangesAsync();

        TempData["Flash"] = $"Password reset for {user.Email}. They will be asked to set their own on next sign-in.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Confirmation step. Deleting an account is not reversible, so it gets a page of its own
    /// rather than a button that acts on the first click.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Delete(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user is null) return NotFound();

        // Same rule as deactivation, for a stronger reason: an administrator who deletes their own
        // account cannot undo it, and if they are the only administrator nobody can create another.
        if (user.Id == _users.GetUserId(User))
        {
            TempData["Flash"] = "You cannot delete your own account.";
            return RedirectToAction(nameof(Index));
        }

        return View(new DeleteUserViewModel
        {
            UserId = user.Id,
            Email = user.Email ?? "",
            DisplayName = user.DisplayName,
            Roles = (await _users.GetRolesAsync(user)).OrderBy(r => r).ToList()
        });
    }

    [HttpPost, ValidateAntiForgeryToken, ActionName(nameof(Delete))]
    public async Task<IActionResult> DeleteConfirmed(DeleteUserViewModel vm)
    {
        if (string.IsNullOrWhiteSpace(vm.UserId)) return NotFound();

        var user = await _users.FindByIdAsync(vm.UserId);
        if (user is null) return NotFound();

        // Repeated on the POST: the GET check only decides what is offered, and an administrator
        // could post this id directly.
        if (user.Id == _users.GetUserId(User))
        {
            TempData["Flash"] = "You cannot delete your own account.";
            return RedirectToAction(nameof(Index));
        }

        // Display-only fields come from the database, never from the form.
        vm.Email = user.Email ?? "";
        vm.DisplayName = user.DisplayName;
        vm.Roles = (await _users.GetRolesAsync(user)).OrderBy(r => r).ToList();

        if (!ModelState.IsValid) return View(vm);

        // Captured before the row goes: afterwards there is nothing left to read it from.
        var email = user.Email;

        var result = await _users.DeleteAsync(user);

        if (!result.Succeeded)
        {
            AddErrors(result, email, "UserDeleteFailed");
            return View(vm);
        }

        // The audit row keeps the address, so history written by this person stays readable even
        // though the account it belonged to no longer exists.
        _audit.Record(nameof(ApplicationUser), vm.UserId, "UserDeleted", newValue: email);
        await _db.SaveChangesAsync();

        TempData["Flash"] = $"{email} has been deleted.";
        return RedirectToAction(nameof(Index));
    }


    /// <summary>
    /// Surfaces Identity's failures as something an administrator can act on, and records the
    /// attempt. Duplicate-account errors are rewritten: the pre-check above catches the ordinary
    /// case, so reaching here means two administrators created the same address at the same moment
    /// and the unique index settled it - which should read as the same friendly message rather
    /// than a raw "DuplicateUserName".
    /// </summary>
    private void AddErrors(IdentityResult result, string? subject, string action = "UserCreateFailed")
    {
        foreach (var error in result.Errors)
        {
            var message = error.Code is "DuplicateUserName" or "DuplicateEmail"
                ? "An account with that email already exists."
                : error.Description;

            ModelState.AddModelError(string.Empty, message);
        }

        _audit.Record(nameof(ApplicationUser), subject ?? "(unknown)", action,
                      newValue: string.Join("; ", result.Errors.Select(e => e.Code)));
    }
}
