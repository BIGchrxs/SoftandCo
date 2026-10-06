using System.ComponentModel.DataAnnotations;

namespace SoftCo.Models;

/// <summary>
/// Someone Soft &amp; Co invoice. Until now the system modelled only money going out - suppliers,
/// their invoices, and the payments against them - so there was nowhere to record who the work is
/// being done for. Gross profit needs both halves, and a tax invoice is not valid without the
/// customer's details on it, so the client is a register in its own right rather than a name
/// typed onto each invoice.
///
/// A client is not a <see cref="Project"/>. One client runs several projects at once - "Ravello
/// 301" and "Ravello 202 &amp; 401" are two projects for one client - which is why the two are
/// separate tables, and why the existing projects are not backfilled into clients by name.
/// </summary>
public class Client
{
    public int Id { get; set; }

    /// <summary>
    /// Short internal handle, as <see cref="Project.Code"/> is. Unique, so one client cannot be
    /// entered twice under two spellings and split its own revenue in half.
    /// </summary>
    [Required, StringLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The registered name that belongs on a tax invoice, where it differs from the name everyone
    /// uses day to day. Blank means the two are the same.
    /// </summary>
    [StringLength(200)]
    public string? LegalName { get; set; }

    [StringLength(200)]
    public string? ContactName { get; set; }

    [StringLength(256), EmailAddress]
    public string? ContactEmail { get; set; }

    [StringLength(50)]
    public string? ContactPhone { get; set; }

    /// <summary>
    /// The client's VAT registration number, not Soft &amp; Co's. Belongs on a tax invoice when
    /// the customer is itself a vendor; blank is perfectly legitimate for a private individual.
    /// </summary>
    [StringLength(20)]
    public string? VatNumber { get; set; }

    [StringLength(50)]
    public string? RegistrationNumber { get; set; }

    // Billing address, split into fields rather than kept as one block of text: an invoice has to
    // lay it out line by line, and the country is what makes the VAT default sensible.
    [StringLength(200)] public string? BillingLine1 { get; set; }
    [StringLength(200)] public string? BillingLine2 { get; set; }
    [StringLength(100)] public string? BillingCity { get; set; }
    [StringLength(100)] public string? BillingProvince { get; set; }
    [StringLength(20)]  public string? BillingPostalCode { get; set; }

    /// <summary>
    /// Defaults to South Africa because nearly every client is local. Not decoration: goods
    /// exported from South Africa are zero-rated, so the country is what distinguishes a
    /// considered <see cref="DefaultVatTreatment"/> from a guessed one.
    /// </summary>
    [Required, StringLength(100)]
    public string BillingCountry { get; set; } = "South Africa";

    /// <summary>
    /// Days from invoice date to due date. Pre-fills a new invoice only - the due date stored on
    /// the invoice is what the client is held to, so changing this never moves a date already
    /// agreed.
    /// </summary>
    [Range(0, 365)]
    public int DefaultPaymentTermsDays { get; set; } = 30;

    /// <summary>
    /// Pre-fills the treatment on a new invoice line. A default only: treatment is stored per
    /// line, because one invoice can carry zero-rated exports and standard-rated local work.
    /// </summary>
    public VatTreatment DefaultVatTreatment { get; set; } = VatTreatment.Standard;

    /// <summary>
    /// Deactivated rather than deleted, as payment contacts are. Issued invoices must keep
    /// pointing at a real client row or the history stops being readable.
    /// </summary>
    public bool IsActive { get; set; } = true;

    [StringLength(2000)]
    public string? Notes { get; set; }

    // --- External accounting system ----------------------------------------------------------
    // Carried from the first migration so that wiring up an accounting integration later is not
    // also a backfill of every client, invoice and receipt. Nothing writes these yet.
    [StringLength(100)] public string? ExternalId { get; set; }
    [StringLength(100)] public string? ExternalReference { get; set; }
    public SyncStatus SyncStatus { get; set; } = SyncStatus.NotSynced;
    public DateTime? LastSyncedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Holds the creator's sign-in name, matching how the rest of the system fills the column of
    /// this name. Not a foreign key, so a deleted account does not erase who entered the client.
    /// </summary>
    [StringLength(450)] public string? CreatedById { get; set; }

    public ICollection<Project> Projects { get; set; } = new List<Project>();

    /// <summary>
    /// The address as an invoice would print it, blank lines dropped. Here rather than in a view
    /// so the screen and the PDF cannot disagree about it.
    /// </summary>
    public IReadOnlyList<string> BillingAddressLines()
    {
        var parts = new[]
        {
            BillingLine1,
            BillingLine2,
            string.Join(", ", new[] { BillingCity, BillingProvince }
                .Where(s => !string.IsNullOrWhiteSpace(s))),
            BillingPostalCode,
            BillingCountry
        };

        return parts.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()).ToList();
    }
}
