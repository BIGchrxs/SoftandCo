using Microsoft.EntityFrameworkCore;
using SoftCo.Data;

namespace SoftCo.Services.Numbering;

public interface IDocumentNumberGenerator
{
    Task<string> NextAsync(DocumentNumberKind kind, CancellationToken cancellationToken = default);
}

/// <summary>
/// Allocates the references Soft &amp; Co issue: PO-2026-0001, INV-2026-0001, CN-2026-0001.
///
/// Backed by PostgreSQL sequences rather than MAX(number) + 1. A read-then-write would let two
/// users saving at the same moment compute the same reference and leave one of them failing on the
/// unique index; nextval is atomic under concurrency. It also never rolls back, so an abandoned
/// transaction burns a number rather than handing it out twice - the correct trade for an
/// identifier that must never be reused, and the rule the SKU specification sets for internal
/// references.
/// </summary>
public sealed class DocumentNumberGenerator : IDocumentNumberGenerator
{
    /// <summary>
    /// The only place a sequence name exists. Private, static and keyed by the enum, so a caller
    /// can ask for a kind of document but can never name a relation - there is no value of
    /// <see cref="DocumentNumberKind"/> that reaches the database as text of the caller's choosing.
    ///
    /// Schema-qualified on purpose: <c>regclass</c> resolves through <c>search_path</c>, so a bare
    /// "po_number_seq" could be shadowed by a sequence of the same name in another schema that
    /// happened to come first.
    /// </summary>
    private static readonly IReadOnlyDictionary<DocumentNumberKind, string> Sequences =
        new Dictionary<DocumentNumberKind, string>
        {
            [DocumentNumberKind.PurchaseOrder]   = "public.po_number_seq",
            [DocumentNumberKind.CustomerInvoice] = "public.invoice_number_seq",
            [DocumentNumberKind.CreditNote]      = "public.credit_note_number_seq"
        };

    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;

    public DocumentNumberGenerator(AppDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<string> NextAsync(
        DocumentNumberKind kind, CancellationToken cancellationToken = default)
    {
        if (!Sequences.TryGetValue(kind, out var sequence))
            throw new ArgumentOutOfRangeException(
                nameof(kind), kind, "No sequence is defined for this document kind.");

        // No SQL is built by concatenation here, and none needs to be. nextval() takes a VALUE of
        // type regclass rather than an identifier, so the sequence name is an ordinary parameter -
        // this interpolated string becomes nextval(CAST($1 AS regclass)), verified against the
        // database. The cast is what makes it legal; without it PostgreSQL rejects a text parameter
        // in that position.
        //
        // The column must be aliased "Value": EF's SqlQuery<T> projects a scalar result from a
        // column of that name and fails with 42703 otherwise.
        var next = await _db.Database
            .SqlQuery<long>($"SELECT nextval(CAST({sequence} AS regclass)) AS \"Value\"")
            .SingleAsync(cancellationToken);

        return DocumentNumberFormat.Format(kind, _clock.GetUtcNow().Year, next);
    }
}
