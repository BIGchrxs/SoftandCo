using SoftCo.Services.Numbering;

namespace SoftCo.Services;

/// <summary>
/// How a sequence value becomes a purchase-order reference.
///
/// Now a shim over <see cref="DocumentNumberFormat"/>, which does the same job for invoices and
/// credit notes as well. Kept rather than deleted because the existing call sites and the PO tests
/// read better for it, and because those tests passing unmodified against the shared implementation
/// is the evidence that moving the logic changed nothing.
/// </summary>
public static class PoNumberFormat
{
    /// <summary>
    /// PO-{year}-{sequence}. Four digits is a minimum width, not a ceiling: order 10,000
    /// renders as PO-2026-10000 rather than truncating or wrapping back to 0000.
    /// </summary>
    public static string Format(int year, long sequence) =>
        DocumentNumberFormat.Format(DocumentNumberKind.PurchaseOrder, year, sequence);
}
