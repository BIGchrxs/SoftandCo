using System.ComponentModel.DataAnnotations;

namespace SoftCo.Models;

/// <summary>
/// Someone a payment request can be sent to - the finance people and approvers who actually
/// release money. Kept as a shared address book rather than free text on each order so the
/// address is typed once, by someone who knows it, instead of re-typed on every invoice.
/// </summary>
public class PaymentContact
{
    public int Id { get; set; }

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(256), EmailAddress]
    public string Email { get; set; } = string.Empty;

    /// <summary>Free text, e.g. "Accounts payable" or "Director - approves over R100k".</summary>
    [StringLength(200)]
    public string? RoleNote { get; set; }

    /// <summary>
    /// Deactivated rather than deleted: past payment requests must keep pointing at a real
    /// contact row so the history stays readable after someone leaves.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [StringLength(450)] public string? CreatedById { get; set; }
}
