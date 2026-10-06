using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using SoftCo.Models;

namespace SoftCo.ViewModels;

// Client invoicing.
// Namespace deliberately matches the other view-model files.

/// <summary>One row of the invoice grid, flattened in the query.</summary>
public class InvoiceRowViewModel
{
    public int Id { get; set; }
    public string? InvoiceNumber { get; set; }
    public CustomerInvoiceStatus Status { get; set; }
    public string ClientName { get; set; } = "";
    public string? ProjectName { get; set; }
    public DateOnly? IssueDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public decimal NetTotal { get; set; }
    public decimal GrandTotal { get; set; }

    public bool IsOverdue(DateOnly today) =>
        DueDate is DateOnly due && due < today
        && Status is CustomerInvoiceStatus.Issued or CustomerInvoiceStatus.PartPaid;
}

/// <summary>
/// The invoice list, paged from day one.
///
/// The UI does not expose a page size yet, and does not need to - but retrofitting pagination onto a
/// grid that already assumes it can load everything is more work than carrying it from the start,
/// and invoices are the table most likely to grow without anyone noticing.
/// </summary>
public class InvoiceListViewModel
{
    public List<InvoiceRowViewModel> Rows { get; set; } = [];

    public CustomerInvoiceStatus? Status { get; set; }
    public int? ClientId { get; set; }
    public int? ProjectId { get; set; }
    public bool OverdueOnly { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int TotalCount { get; set; }

    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;

    public List<SelectListItem> Clients { get; set; } = [];

    /// <summary>
    /// Totals for the rows on screen, not for the whole filtered set. Said plainly on the page,
    /// because a figure that silently means "this page only" is worse than no figure.
    /// </summary>
    public decimal PageGrandTotal => Rows.Sum(r => r.GrandTotal);
}

/// <summary>One editable line on the invoice form.</summary>
public class InvoiceLineViewModel
{
    [StringLength(500)]
    public string? Description { get; set; }

    [Range(0, 1000000)]
    public decimal Quantity { get; set; } = 1m;

    /// <summary>
    /// Exclusive or inclusive of VAT depending on <see cref="InvoiceEditViewModel.PricesEnteredInclusive"/>.
    /// Converted once at save; what gets stored is always exclusive.
    /// </summary>
    [Range(0, 1000000000)]
    public decimal UnitPrice { get; set; }

    public VatTreatment VatTreatment { get; set; } = VatTreatment.Standard;
}

/// <summary>The invoice form. Only ever edits a draft.</summary>
public class InvoiceEditViewModel : IValidatableObject
{
    public int Id { get; set; }

    [Required, Display(Name = "Client")]
    public int ClientId { get; set; }

    [Display(Name = "Project")]
    public int? ProjectId { get; set; }

    [Range(0, 365), Display(Name = "Payment terms (days)")]
    public int PaymentTermsDays { get; set; } = 30;

    [StringLength(100), Display(Name = "Client reference")]
    public string? ClientReference { get; set; }

    [Display(Name = "Prices include VAT")]
    public bool PricesEnteredInclusive { get; set; }

    [StringLength(2000), Display(Name = "Notes (printed on the invoice)")]
    public string? Notes { get; set; }

    [StringLength(2000), Display(Name = "Internal notes (never printed)")]
    public string? InternalNotes { get; set; }

    public List<InvoiceLineViewModel> Lines { get; set; } = [];

    // --- Filled by the controller ------------------------------------------------------------
    public List<SelectListItem> Clients { get; set; } = [];
    public List<SelectListItem> Projects { get; set; } = [];
    public CustomerInvoiceStatus Status { get; set; } = CustomerInvoiceStatus.Draft;
    public decimal StandardRatePercent { get; set; }
    public bool IsNew => Id == 0;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var filled = Lines.Where(l => !string.IsNullOrWhiteSpace(l.Description)).ToList();

        // Allowed to have no lines while being drafted - an invoice is often started before anyone
        // knows what goes on it. Submitting for approval is where that is refused.
        foreach (var line in filled.Where(l => l.Quantity <= 0))
            yield return new ValidationResult(
                $"\"{line.Description}\" needs a quantity greater than zero.", [nameof(Lines)]);

        // A line with a description and no price is almost always a half-finished thought rather
        // than a deliberate freebie, and it is silent on the invoice.
        foreach (var line in filled.Where(l => l.UnitPrice <= 0))
            yield return new ValidationResult(
                $"\"{line.Description}\" has no price. Remove the line if it is not being charged for.",
                [nameof(Lines)]);
    }
}

/// <summary>The read-only invoice screen.</summary>
public class InvoiceDetailsViewModel
{
    public CustomerInvoice Invoice { get; set; } = null!;
    public string ClientName { get; set; } = "";
    public string? ProjectName { get; set; }
    public bool CanEdit { get; set; }
    public string? WhyNotEditable { get; set; }
    public DateOnly Today { get; set; }
}
