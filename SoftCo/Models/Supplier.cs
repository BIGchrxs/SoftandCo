using System.ComponentModel.DataAnnotations;

namespace SoftCo.Models;

public class Supplier
{
    public int Id { get; set; }

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    public SupplierType Type { get; set; } = SupplierType.Manufacturer;

    /// <summary>
    /// Local (South African, invoices in Rand) or international (overseas, ships and clears).
    /// Separate from <see cref="Type"/>: a freight agent can be either, and so can a manufacturer.
    /// </summary>
    public SupplierOrigin Origin { get; set; } = SupplierOrigin.International;

    [StringLength(100)]
    public string? Country { get; set; }

    /// <summary>
    /// Pre-fills the currency on a new order for this supplier. Only a default — the rate and
    /// currency that matter are the ones stored on each invoice.
    /// </summary>
    [StringLength(3)]
    public string DefaultCurrencyCode { get; set; } = "CNY";

    [StringLength(200)]
    public string? ContactName { get; set; }

    [StringLength(200)]
    public string? ContactEmail { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [StringLength(450)] public string? CreatedById { get; set; }

    public ICollection<SupplierOrder> Orders { get; set; } = new List<SupplierOrder>();
}
