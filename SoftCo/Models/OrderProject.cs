using System.ComponentModel.DataAnnotations.Schema;

namespace SoftCo.Models;

/// <summary>
/// Join between an order and the project(s) it serves. Many-to-many from day one because the
/// existing tracker already has single lines spanning several projects - "Williams / Ravello 301",
/// "Arcadia / Williams", "Inospace / Inhouse Office Chairs" - and splitting that out later, once
/// live data exists, is far more painful than carrying the join table now.
/// </summary>
public class OrderProject
{
    public int SupplierOrderId { get; set; }
    public SupplierOrder? SupplierOrder { get; set; }

    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    /// <summary>
    /// How much of the order's cost belongs to this project, as a fraction between 0 and 1.
    ///
    /// <para>This column is what makes gross profit possible. Without it the join carried no amount,
    /// so an order serving two projects had its <b>full</b> value counted into <b>each</b> - which
    /// is why the README has always warned that project balances are not additive. A margin built on
    /// that cost base would have been confidently wrong, and wrong in the direction that flatters.</para>
    ///
    /// <para>Backfilled as an even split, which is exactly right for the roughly thirty orders that
    /// serve a single project and a defensible starting point for the seven that do not. A database
    /// check constraint keeps it in range; the rule that an order's shares sum to 1 is enforced in
    /// the service, because it is a statement about a set of rows rather than about one.</para>
    /// </summary>
    [Column(TypeName = "numeric(9,6)")]
    public decimal AllocationShare { get; set; } = 1m;
}
