using Microsoft.EntityFrameworkCore;
using SoftCo.Data;

namespace SoftCo.Services;

public interface IPoNumberGenerator
{
    Task<string> NextAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Allocates internal purchase-order numbers in the form PO-2026-0001.
///
/// Backed by a Postgres sequence rather than MAX(PoNumber) + 1. A read-then-write would let two
/// users saving at the same moment compute the same number and leave one of them failing on the
/// unique index; nextval is atomic under concurrency. It also never rolls back, so an abandoned
/// transaction burns a number rather than handing it out twice - which is the correct trade for
/// an identifier that must never be reused, and matches the rule the SKU specification sets for
/// internal references.
/// </summary>
public sealed class PoNumberGenerator : IPoNumberGenerator
{
    public const string SequenceName = "po_number_seq";

    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;

    public PoNumberGenerator(AppDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<string> NextAsync(CancellationToken cancellationToken = default)
    {
        // The column must be aliased "Value": EF's SqlQuery<T> projects a scalar result from a
        // column of that name and fails with 42703 otherwise.
        var next = await _db.Database
            .SqlQuery<long>($"SELECT nextval('po_number_seq') AS \"Value\"")
            .SingleAsync(cancellationToken);

        return PoNumberFormat.Format(_clock.GetUtcNow().Year, next);
    }
}
