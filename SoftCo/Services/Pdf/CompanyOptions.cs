using System.ComponentModel.DataAnnotations;

namespace SoftCo.Services.Pdf;

/// <summary>
/// Soft &amp; Co's own details, as they appear on anything the company issues.
///
/// Configuration rather than a database row because these are deployment facts, not data staff
/// edit: the address on a tax invoice changes when the company moves, which is a release, not a
/// Tuesday afternoon. Bound from the "Company" section of appsettings, so staging can issue
/// obviously-marked documents without touching the code.
/// </summary>
public sealed class CompanyOptions
{
    public const string SectionName = "Company";

    public string Name { get; set; } = "Soft & Co.";

    /// <summary>The registered name, where it differs from the trading name.</summary>
    public string? LegalName { get; set; }

    public string[] AddressLines { get; set; } = [];

    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? RegistrationNumber { get; set; }

    /// <summary>
    /// Soft &amp; Co's own VAT number. Printed on a purchase order as a matter of form; from Phase 6
    /// it is also what makes an outgoing document a valid tax invoice, which is why
    /// <see cref="IsVatRegistered"/> is a flag rather than an inference from this being non-empty -
    /// a blank number on a registered vendor is a mistake worth seeing, not a silent downgrade to
    /// an ordinary invoice.
    /// </summary>
    public string? VatNumber { get; set; }

    public bool IsVatRegistered { get; set; } = true;

    /// <summary>
    /// The standard rate applied to <b>new</b> invoice lines. 15% in South Africa at the time of
    /// writing; it was 14% until 2018 and a further rise was tabled and withdrawn in 2025.
    ///
    /// Configuration, never a constant in the code - and read only when a line is created. Once
    /// saved, the rate is frozen on the line, so changing this re-prices nothing that already
    /// exists. That is the whole reason it is stored per line rather than looked up at render time.
    /// </summary>
    [Range(0, 100)]
    public decimal StandardVatRatePercent { get; set; } = 15m;

    /// <summary>The letterhead's address block, blanks dropped.</summary>
    public IReadOnlyList<string> LetterheadLines()
    {
        var lines = AddressLines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToList();

        var contact = string.Join("   ", new[] { Phone, Email }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (contact.Length > 0) lines.Add(contact);

        return lines;
    }
}
