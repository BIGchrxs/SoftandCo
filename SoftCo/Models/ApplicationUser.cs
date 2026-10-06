using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace SoftCo.Models;

public class ApplicationUser : IdentityUser
{
    [StringLength(100)] public string? FirstName { get; set; }
    [StringLength(100)] public string? LastName { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Set when an administrator creates the account or resets its password. While it is true the
    /// user is held on the change-password screen and can reach nothing else.
    ///
    /// The point is that a password an administrator typed is known to at least two people. It is
    /// a way in, not a credential, and it stops being usable the moment the owner replaces it.
    /// </summary>
    public bool MustChangePassword { get; set; }

    public DateTime? LastLoginAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string DisplayName =>
        string.IsNullOrWhiteSpace(FirstName) && string.IsNullOrWhiteSpace(LastName)
            ? (UserName ?? Email ?? "Unknown")
            : $"{FirstName} {LastName}".Trim();
}
