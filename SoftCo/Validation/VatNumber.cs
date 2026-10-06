namespace SoftCo.Validation;

/// <summary>
/// South African VAT registration number format. Pure and dependency-free, so it is unit-tested
/// alongside the other validation rules and the same rule that checks a client's number can later
/// check Soft &amp; Co's own on an outgoing tax invoice.
///
/// A SARS VAT number is exactly ten digits beginning with 4. No checksum for it is published, so
/// format is as far as validation can honestly go: this catches a transposed, truncated or
/// mistyped number, not an invented one.
/// </summary>
public static class VatNumberRules
{
    public const string Message =
        "A South African VAT number is 10 digits starting with 4. Leave it blank if the client is not VAT registered.";

    /// <summary>
    /// Strips the spaces people paste in from letterheads. Returns null when there is no number at
    /// all, so callers get one representation of "absent" rather than three.
    /// </summary>
    public static string? Normalise(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var stripped = new string(value.Where(ch => !char.IsWhiteSpace(ch)).ToArray());
        return stripped.Length == 0 ? null : stripped;
    }

    /// <summary>
    /// True when the value is a well-formed South African VAT number. A blank number passes:
    /// emptiness is not this rule's business, and a client who is not VAT registered has none.
    /// </summary>
    public static bool IsValidSouthAfrican(string? value)
    {
        var v = Normalise(value);
        if (v is null) return true;

        return v.Length == 10 && v[0] == '4' && v.All(char.IsAsciiDigit);
    }

    /// <summary>
    /// Whether a billing country means South Africa, and therefore whether the SA number format
    /// and the standard rate apply. Accepts the handful of spellings people actually type; anything
    /// else is treated as foreign, which is the safe direction - it relaxes a format check rather
    /// than applying the wrong one.
    /// </summary>
    public static bool IsSouthAfrica(string? country) =>
        Normalise(country)?.ToLowerInvariant() is "southafrica" or "rsa" or "za" or "republicofsouthafrica";
}
