namespace SoftCo.Services;

/// <summary>
/// How a sequence value becomes a purchase-order reference. Kept free of any database or
/// framework dependency so the formatting rules can be tested on their own.
/// </summary>
public static class PoNumberFormat
{
    /// <summary>
    /// PO-{year}-{sequence}. Four digits is a minimum width, not a ceiling: order 10,000
    /// renders as PO-2026-10000 rather than truncating or wrapping back to 0000.
    /// </summary>
    public static string Format(int year, long sequence) => $"PO-{year}-{sequence:0000}";
}
