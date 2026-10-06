using System.ComponentModel.DataAnnotations;

namespace SoftCo.Models;

/// <summary>
/// A client engagement goods are bought against — Fairmont, Ravello 202 &amp; 401, Williams,
/// De Wet, Arcadia, Estate Penthouse, T.M Mauritius, Harbour House V&amp;A in the current tracker.
/// Budgets and milestones still come later and hang off this same row. The client record no
/// longer does - see <see cref="ClientId"/>.
/// </summary>
public class Project
{
    public int Id { get; set; }

    [Required, StringLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Who the project is for. Nullable, and deliberately not backfilled: the register came from a
    /// spreadsheet in which "Ravello", "Ravello 301" and "Ravello 202 &amp; 401" are three
    /// projects for one client, while "Samples" and "Soft &amp; Co Stock" are internal and have no
    /// client at all. Auto-creating a client per project would mint three duplicate Ravellos and
    /// two fictional clients, indistinguishable from real ones from then on.
    ///
    /// New projects must name a client - enforced in the edit view model, never in the database,
    /// so the rows already there stay valid and get assigned by hand.
    /// </summary>
    public int? ClientId { get; set; }
    public Client? Client { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<OrderProject> OrderProjects { get; set; } = new List<OrderProject>();
}
