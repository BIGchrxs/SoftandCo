using System.ComponentModel.DataAnnotations;
using SoftCo.Models;

namespace SoftCo.ViewModels;

// Signing in, and a person's own account.
// Namespace deliberately matches the other view-model files, so splitting them changed
// no controller, view or using directive.

public class LoginViewModel
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = "";

    // Bounded because this endpoint is unauthenticated: without a cap, an arbitrarily long string
    // reaches the password hasher on every request anyone cares to make. Deliberately NOT the
    // stricter SignInEmail rule - the login form should not become a way to probe which address
    // shapes the system considers real.
    [Required, DataType(DataType.Password), StringLength(128)]
    public string Password { get; set; } = "";

    public string? ReturnUrl { get; set; }
}

public class ChangePasswordViewModel
{
    // Bounded like every other password field. An unbounded string here is handed straight to the
    // password hasher, which is work the server does on request.
    [Required, DataType(DataType.Password), StringLength(128)]
    [Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = "";

    [Required, DataType(DataType.Password), StringLength(128, MinimumLength = 12)]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = "";

    [Required, DataType(DataType.Password), Display(Name = "Confirm new password")]
    [Compare(nameof(NewPassword), ErrorMessage = "The two passwords do not match.")]
    public string ConfirmPassword { get; set; } = "";
}

/// <summary>
/// A person's own account page. Only the name fields are bound back - email, roles and status are
/// shown but never posted, so a crafted form cannot be used to grant oneself a role or reactivate
/// a disabled account.
/// </summary>
public class ProfileViewModel
{
    [StringLength(100), Display(Name = "First name")]
    public string? FirstName { get; set; }

    [StringLength(100), Display(Name = "Last name")]
    public string? LastName { get; set; }

    // --- Shown, never accepted ---------------------------------------------------------------
    public string Email { get; set; } = "";
    public List<string> Roles { get; set; } = [];
    public bool IsActive { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
