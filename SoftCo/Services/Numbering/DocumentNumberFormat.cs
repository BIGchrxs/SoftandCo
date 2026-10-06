namespace SoftCo.Services.Numbering;

/// <summary>
/// The documents Soft &amp; Co number for themselves. Each one draws from its own sequence, so a
/// credit note cannot consume an invoice number.
///
/// Values are explicit and must never be renumbered: they key the dictionary of sequence names in
/// <see cref="DocumentNumberGenerator"/>, and a reordering would silently point one document kind
/// at another's sequence.
/// </summary>
public enum DocumentNumberKind
{
    PurchaseOrder   = 0,
    CustomerInvoice = 1,
    CreditNote      = 2
}

/// <summary>
/// How a sequence value becomes a document reference. Kept free of any database or framework
/// dependency - the enum lives here rather than beside the other enums for exactly that reason, so
/// one linked file carries the whole unit into the test project.
/// </summary>
public static class DocumentNumberFormat
{
    /// <summary>
    /// The letters in front of the year. Deliberately a switch with no default fallback to
    /// <c>kind.ToString()</c>: a new document kind must fail loudly here rather than quietly
    /// issuing references prefixed "SomeNewKind".
    /// </summary>
    public static string Prefix(DocumentNumberKind kind) => kind switch
    {
        DocumentNumberKind.PurchaseOrder   => "PO",
        DocumentNumberKind.CustomerInvoice => "INV",
        DocumentNumberKind.CreditNote      => "CN",
        _ => throw new ArgumentOutOfRangeException(
                 nameof(kind), kind, "No prefix is defined for this document kind.")
    };

    /// <summary>
    /// {prefix}-{year}-{sequence}, e.g. PO-2026-0001, INV-2026-0001, CN-2026-0001.
    ///
    /// Four digits is a minimum width, not a ceiling: document 10,000 renders as INV-2026-10000
    /// rather than truncating or wrapping back to 0000.
    ///
    /// The sequence does not restart each January - it is global to the document kind - so the year
    /// is a label on the reference rather than part of its uniqueness. That is how purchase orders
    /// have always behaved here, and it means a reference stays unique even if a document is issued
    /// either side of midnight on New Year.
    /// </summary>
    public static string Format(DocumentNumberKind kind, int year, long sequence) =>
        $"{Prefix(kind)}-{year}-{sequence:0000}";
}
