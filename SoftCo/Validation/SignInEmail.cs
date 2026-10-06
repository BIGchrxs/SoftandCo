using System.ComponentModel.DataAnnotations;

namespace SoftCo.Validation;

/// <summary>
/// Validates an address that will also become an Identity username.
///
/// [EmailAddress] on its own is not enough here, for two reasons that pull in opposite directions:
///
/// 1. It is more permissive than it looks. It accepts "a@b" (no top-level domain) and even
///    "a b@x.com" with a space in it. An address with no real domain creates an account nobody can
///    be contacted on, which starts to matter the moment password reset goes through email.
///
/// 2. It is also more permissive than Identity. The default AllowedUserNameCharacters is
///    a-z A-Z 0-9 - . _ @ +, which is narrower than real email syntax, so a perfectly legitimate
///    address like o'brien@example.com passes the attribute and is then rejected deep inside
///    CreateAsync with "Username ... can only contain letters or digits" - an error about a field
///    the administrator never filled in.
///
/// Checking both here means the rejection happens on the field the person typed, with a message
/// that says what is actually wrong.
/// </summary>
public static class SignInEmailRules
{
    /// <summary>Identity's default IdentityOptions.User.AllowedUserNameCharacters.</summary>
    public const string AllowedCharacters =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+";

    public const string CharacterMessage =
        "An email address used to sign in may only contain letters, digits and - . _ @ +";

    public const string ShapeMessage =
        "Enter a complete email address, for example name@softandco.co.za";

    /// <summary>Null when the address is usable; otherwise the reason it is not.</summary>
    public static string? Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null; // [Required] owns emptiness.

        var email = value.Trim();

        if (email.Length > 256)
            return "That email address is too long.";

        if (email.Any(c => !AllowedCharacters.Contains(c)))
            return CharacterMessage;

        // Exactly one @, something before it, and a dotted domain after it with a real suffix.
        var parts = email.Split('@');
        if (parts.Length != 2) return ShapeMessage;

        var (local, domain) = (parts[0], parts[1]);
        if (local.Length == 0 || domain.Length == 0) return ShapeMessage;

        var labels = domain.Split('.');
        if (labels.Length < 2 || labels.Any(l => l.Length == 0)) return ShapeMessage;

        // A domain cannot end in a digits-only or single-character suffix in practice.
        if (labels[^1].Length < 2) return ShapeMessage;

        return null;
    }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class SignInEmailAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext context)
    {
        var error = SignInEmailRules.Validate(value as string);

        return error is null
            ? ValidationResult.Success
            : new ValidationResult(error, [context.MemberName ?? ""]);
    }
}
