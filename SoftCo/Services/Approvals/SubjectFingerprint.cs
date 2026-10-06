using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SoftCo.Services.Approvals;

/// <summary>
/// A hash of the facts an approver actually weighed up.
///
/// Taken when a purchase order is submitted and checked again before it can be issued. If the order
/// has been edited in between, the hashes differ and it must go back for approval - which is the
/// only thing standing between "the Financial Director approved R318,622.26" and an order that
/// quietly left at R400,000.
///
/// <para><b>What is deliberately not in the hash.</b> The product description and the notes. The
/// plan anticipated that typo fixes would otherwise keep resetting approvals, and an approver is
/// agreeing to an amount payable to a supplier for a project - not to a spelling. Everything that
/// changes what Soft &amp; Co owes, or to whom, is in.</para>
///
/// Pure and dependency-free, so the canonical form can be asserted on directly.
/// </summary>
public static class SubjectFingerprint
{
    /// <summary>
    /// The exact text that gets hashed. Exposed because a fingerprint mismatch is otherwise two
    /// opaque hex strings, and a test that only compares hashes cannot say which field moved.
    /// </summary>
    public static string CanonicalForm(
        int supplierId,
        string currencyCode,
        decimal exchangeRate,
        decimal invoiceValueForeign,
        decimal invoiceValueZar,
        IEnumerable<int> projectIds)
    {
        // Projects are sorted and de-duplicated: the set of projects is material, the order in
        // which EF happened to return them is not. Without this, reloading an order could produce
        // a different hash from the same data.
        var projects = string.Join(",", projectIds.Distinct().OrderBy(id => id));

        // InvariantCulture throughout. A machine on a comma-decimal locale would otherwise hash
        // "2,4815" where another hashed "2.4815", and every approval would invalidate itself the
        // moment it was checked on a different host.
        var c = CultureInfo.InvariantCulture;

        return string.Join("|",
            "po.v1",
            supplierId.ToString(c),
            (currencyCode ?? "").Trim().ToUpperInvariant(),
            exchangeRate.ToString("F6", c),
            invoiceValueForeign.ToString("F2", c),
            invoiceValueZar.ToString("F2", c),
            projects);
    }

    /// <summary>SHA-256 of <see cref="CanonicalForm"/>, lower-case hex.</summary>
    public static string ForPurchaseOrder(
        int supplierId,
        string currencyCode,
        decimal exchangeRate,
        decimal invoiceValueForeign,
        decimal invoiceValueZar,
        IEnumerable<int> projectIds)
    {
        var canonical = CanonicalForm(supplierId, currencyCode, exchangeRate,
                                      invoiceValueForeign, invoiceValueZar, projectIds);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    /// <summary>
    /// Whether a stored fingerprint still describes the order.
    ///
    /// A missing stored fingerprint returns false rather than true. An approval that cannot prove
    /// what it covered must not be treated as covering whatever is there now.
    /// </summary>
    public static bool Matches(string? stored, string current) =>
        !string.IsNullOrEmpty(stored) &&
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(stored), Encoding.UTF8.GetBytes(current));
}
