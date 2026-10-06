using SoftCo.Services.Numbering;

namespace SoftCo.Services;

public interface IPoNumberGenerator
{
    Task<string> NextAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Allocates internal purchase-order numbers in the form PO-2026-0001.
///
/// Now a delegate to <see cref="IDocumentNumberGenerator"/>, which allocates invoice and credit-note
/// references from their own sequences by the same mechanism. The reasoning that chose that
/// mechanism - a PostgreSQL sequence rather than MAX(PoNumber) + 1, atomic under concurrency and
/// never reused - lives with it there.
///
/// This interface stays because the ordering code is written against it and the name says what it
/// allocates at the call site. It owns no logic of its own.
/// </summary>
public sealed class PoNumberGenerator : IPoNumberGenerator
{
    /// <summary>
    /// The unqualified sequence name, kept for the migration and for anything that reports on
    /// numbering. The generator itself uses the schema-qualified name from its own table.
    /// </summary>
    public const string SequenceName = "po_number_seq";

    private readonly IDocumentNumberGenerator _numbers;

    public PoNumberGenerator(IDocumentNumberGenerator numbers) => _numbers = numbers;

    public Task<string> NextAsync(CancellationToken cancellationToken = default) =>
        _numbers.NextAsync(DocumentNumberKind.PurchaseOrder, cancellationToken);
}
