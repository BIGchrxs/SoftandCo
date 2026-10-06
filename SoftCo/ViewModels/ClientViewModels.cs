using System.ComponentModel.DataAnnotations;
using SoftCo.Models;
using SoftCo.Validation;

namespace SoftCo.ViewModels;

// The client register's edit form.
// Namespace deliberately matches the other view-model files, so splitting them changed no
// controller, view or using directive.

/// <summary>
/// Stands between the form and <see cref="Client"/> for two reasons. The accounting-sync columns
/// and the created-by pair are not the form's to set, and binding the entity directly on Create
/// would persist whatever a crafted post put in them. And the rules that depend on more than one
/// field - a South African VAT number, a South African client billed at 0% - need
/// <see cref="IValidatableObject"/>, which does not belong on a persisted entity.
/// </summary>
public class ClientEditViewModel : IValidatableObject
{
    public int Id { get; set; }

    [Required, StringLength(50), Display(Name = "Code")]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(200), Display(Name = "Name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(200), Display(Name = "Legal name")]
    public string? LegalName { get; set; }

    [StringLength(200), Display(Name = "Contact")]
    public string? ContactName { get; set; }

    [StringLength(256), EmailAddress, Display(Name = "Contact email")]
    public string? ContactEmail { get; set; }

    [StringLength(50), Display(Name = "Phone")]
    public string? ContactPhone { get; set; }

    [StringLength(20), Display(Name = "VAT number")]
    public string? VatNumber { get; set; }

    [StringLength(50), Display(Name = "Registration number")]
    public string? RegistrationNumber { get; set; }

    [StringLength(200), Display(Name = "Address line 1")] public string? BillingLine1 { get; set; }
    [StringLength(200), Display(Name = "Address line 2")] public string? BillingLine2 { get; set; }
    [StringLength(100), Display(Name = "City")]           public string? BillingCity { get; set; }
    [StringLength(100), Display(Name = "Province")]       public string? BillingProvince { get; set; }
    [StringLength(20),  Display(Name = "Postal code")]    public string? BillingPostalCode { get; set; }

    [Required, StringLength(100), Display(Name = "Country")]
    public string BillingCountry { get; set; } = "South Africa";

    [Range(0, 365), Display(Name = "Payment terms (days)")]
    public int DefaultPaymentTermsDays { get; set; } = 30;

    [Display(Name = "Default VAT treatment")]
    public VatTreatment DefaultVatTreatment { get; set; } = VatTreatment.Standard;

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    [StringLength(2000), Display(Name = "Notes")]
    public string? Notes { get; set; }

    // --- Shown, never bound ------------------------------------------------------------------
    // Displayed so someone can see whether the client has reached the accounting system. Posting
    // them back would be ignored: ApplyTo does not copy them.

    public SyncStatus SyncStatus { get; set; } = SyncStatus.NotSynced;

    /// <summary>How many projects already point at this client. Nil for a new one.</summary>
    public int ProjectCount { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var inSouthAfrica = VatNumberRules.IsSouthAfrica(BillingCountry);

        // Checked only for South African clients: a Mauritian or UK registration follows its own
        // format entirely, and applying the SARS rule to it would make foreign clients - the
        // exact ones whose invoices are zero-rated - impossible to enter.
        if (inSouthAfrica && !VatNumberRules.IsValidSouthAfrican(VatNumber))
            yield return new ValidationResult(VatNumberRules.Message, [nameof(VatNumber)]);

        // Zero-rating is for exports. A South African client defaulted to 0% is almost always a
        // mistake, and it is the kind nobody notices until SARS does.
        if (inSouthAfrica && DefaultVatTreatment == VatTreatment.ZeroRated)
            yield return new ValidationResult(
                "Zero-rating applies to exported goods. A client billed in South Africa is standard-rated. " +
                "Change the country if these goods are being exported.",
                [nameof(DefaultVatTreatment)]);
    }

    public static ClientEditViewModel FromEntity(Client c, int projectCount = 0) => new()
    {
        Id = c.Id,
        Code = c.Code,
        Name = c.Name,
        LegalName = c.LegalName,
        ContactName = c.ContactName,
        ContactEmail = c.ContactEmail,
        ContactPhone = c.ContactPhone,
        VatNumber = c.VatNumber,
        RegistrationNumber = c.RegistrationNumber,
        BillingLine1 = c.BillingLine1,
        BillingLine2 = c.BillingLine2,
        BillingCity = c.BillingCity,
        BillingProvince = c.BillingProvince,
        BillingPostalCode = c.BillingPostalCode,
        BillingCountry = c.BillingCountry,
        DefaultPaymentTermsDays = c.DefaultPaymentTermsDays,
        DefaultVatTreatment = c.DefaultVatTreatment,
        IsActive = c.IsActive,
        Notes = c.Notes,
        SyncStatus = c.SyncStatus,
        ProjectCount = projectCount
    };

    /// <summary>
    /// Copies the editable fields onto the entity, normalising as it goes. The sync columns,
    /// <see cref="Client.CreatedAt"/> and <see cref="Client.CreatedById"/> are deliberately absent:
    /// they are not the form's to set.
    /// </summary>
    public void ApplyTo(Client c)
    {
        c.Code = Code.Trim().ToUpperInvariant();
        c.Name = Name.Trim();
        c.LegalName = Blank(LegalName);
        c.ContactName = Blank(ContactName);
        c.ContactEmail = Blank(ContactEmail)?.ToLowerInvariant();
        c.ContactPhone = Blank(ContactPhone);
        c.VatNumber = VatNumberRules.Normalise(VatNumber);
        c.RegistrationNumber = Blank(RegistrationNumber);
        c.BillingLine1 = Blank(BillingLine1);
        c.BillingLine2 = Blank(BillingLine2);
        c.BillingCity = Blank(BillingCity);
        c.BillingProvince = Blank(BillingProvince);
        c.BillingPostalCode = Blank(BillingPostalCode);
        c.BillingCountry = BillingCountry.Trim();
        c.DefaultPaymentTermsDays = DefaultPaymentTermsDays;
        c.DefaultVatTreatment = DefaultVatTreatment;
        c.IsActive = IsActive;
        c.Notes = Blank(Notes);
    }

    /// <summary>An empty box means "no value", not an empty string. Keeps the nulls honest.</summary>
    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
