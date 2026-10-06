namespace SoftCo.Services.Pdf;

/// <summary>
/// The brand as a printed document uses it, taken from the same tokens as
/// <c>wwwroot/css/site.css</c> so a purchase order and the screen that produced it are recognisably
/// the same system.
///
/// Deliberately library-free - hex strings and font names, no MigraDoc types. Only
/// <see cref="MigraDocPdfRenderer"/> knows PDFsharp exists, and keeping that true means this file
/// stays readable next to the stylesheet it mirrors.
/// </summary>
public static class PdfBrand
{
    // --- Colour, matching site.css ------------------------------------------------------------
    public const string Accent = "#552403";      // --sc-accent, the burgundy
    public const string AccentStrong = "#401B02";// --sc-accent-strong
    public const string Ink = "#332F2B";         // --sc-ink, body text
    public const string InkSoft = "#766F65";     // --sc-ink-soft, captions and labels
    public const string Beige = "#F8ECD6";       // --sc-sidebar-bg, the warm beige
    public const string BeigeDeep = "#F1E1C4";   // --sc-beige-deep, table header fill
    public const string Rule = "#D8D2CA";        // a printable stand-in for --sc-line, which is
                                                 // an alpha value CSS can blend and PDF cannot

    // --- Type ---------------------------------------------------------------------------------
    // Two faces, both SIL OFL and therefore embeddable. Cremore is the brand display face but is
    // unlicensed here, so Cormorant Garamond carries the display role exactly as it does on screen.

    /// <summary>Headings and the wordmark.</summary>
    public const string DisplayFont = "Cormorant Garamond";

    /// <summary>Body text, labels, and every figure - numbers never use the display face.</summary>
    public const string SansFont = "Manrope";

    // --- Geometry -----------------------------------------------------------------------------
    public const double PageMarginCm = 2.0;
    public const double LabelTrackingPt = 1.2;   // the deck's widely-tracked micro-labels
    public const double BodySizePt = 9.5;
    public const double LabelSizePt = 7.5;
    public const double TitleSizePt = 22;
}
