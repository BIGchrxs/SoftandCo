using System.ComponentModel.DataAnnotations;
using SoftCo.Models;
using SoftCo.Validation;

namespace SoftCo.ViewModels;

// Administering staff accounts.
// Namespace deliberately matches the other view-model files, so splitting them changed
// no controller, view or using directive.

public class UserRowViewModel
{
    public string Id { get; set; } = "";
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool IsActive { get; set; }
    public bool MustChangePassword { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<string> Roles { get; set; } = [];

    /// <summary>Used to hide the controls an administrator must not point at themselves.</summary>
    public bool IsSelf { get; set; }
}

public class CreateUserViewModel
{
    // SignInEmail rather than EmailAddress: this value also becomes the Identity username, and
    // the two have different ideas about what is acceptable. See SignInEmailRules.
    [Required, SignInEmail, StringLength(256)]
    public string Email { get; set; } = "";

    [StringLength(100), Display(Name = "First name")]
    public string? FirstName { get; set; }

    [StringLength(100), Display(Name = "Last name")]
    public string? LastName { get; set; }

    /// <summary>
    /// A way in, not a lasting credential: the account is flagged so the user has to replace it
    /// before reaching anything. Length is checked here for a friendly message; the real policy
    /// is enforced by Identity in CreateAsync.
    /// </summary>
    [Required, DataType(DataType.Password), StringLength(128, MinimumLength = 12)]
    [Display(Name = "Temporary password")]
    public string Password { get; set; } = "";

    /// <summary>
    /// The administrator cannot see what they typed, and the person receiving it cannot tell a
    /// mistyped password from a mistyped one at their end. Without this, the usual resolution is
    /// someone pasting the password into chat to compare - which is exactly what the forced change
    /// exists to avoid.
    /// </summary>
    [Required, DataType(DataType.Password), StringLength(128)]
    [Display(Name = "Confirm temporary password")]
    [Compare(nameof(Password), ErrorMessage = "The two passwords do not match.")]
    public string ConfirmPassword { get; set; } = "";

    /// <summary>
    /// Staff for everyone doing the work; Financial Director for the person who approves it.
    ///
    /// Offered as a choice because the two are deliberately exclusive: an account that could both
    /// raise and approve an order would make "the approver is not the submitter" unenforceable.
    /// Validated against <see cref="SoftCo.Data.Roles.Assignable"/> in the controller, never
    /// trusted as a role name straight off the form.
    /// </summary>
    [Required, Display(Name = "Access level")]
    public string Role { get; set; } = SoftCo.Data.Roles.Staff;
}

public class ResetUserPasswordViewModel
{
    [Required] public string UserId { get; set; } = "";
    public string Email { get; set; } = "";

    [Required, DataType(DataType.Password), StringLength(128, MinimumLength = 12)]
    [Display(Name = "Temporary password")]
    public string Password { get; set; } = "";

    [Required, DataType(DataType.Password), StringLength(128)]
    [Display(Name = "Confirm temporary password")]
    [Compare(nameof(Password), ErrorMessage = "The two passwords do not match.")]
    public string ConfirmPassword { get; set; } = "";
}

/// <summary>
/// Confirmation of an irreversible action. Only the id is bound back; the name, address and roles
/// are re-read from the database for display, so the page cannot be made to describe one account
/// while deleting another.
/// </summary>
public class DeleteUserViewModel
{
    [Required] public string UserId { get; set; } = "";

    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public List<string> Roles { get; set; } = [];
}
