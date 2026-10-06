using System.Collections.Concurrent;
using System.Reflection;
using PdfSharp.Fonts;

namespace SoftCo.Services.Pdf;

/// <summary>
/// Supplies the brand typefaces to PDFsharp from files embedded in this assembly.
///
/// PDFsharp 6 on .NET Core has no access to system fonts and throws unless a resolver is set. On
/// Windows that looks like an inconvenience; in the Linux container it is the whole problem, and it
/// is the classic way a PDF feature passes locally and fails in production.
///
/// Embedding turns that into an advantage. Manrope and Cormorant Garamond are both SIL Open Font
/// Licence, which expressly permits embedding, so the bytes travel inside the assembly: the
/// document renders in the real brand faces, identically on a developer's machine and in the
/// container, and the Dockerfile needs no font package installed.
///
/// Registered once at startup via <c>GlobalFontSettings.FontResolver</c>, which is process-wide
/// static state in PDFsharp - hence the single assignment in Program.cs rather than a DI lifetime.
/// </summary>
public sealed class EmbeddedFontResolver : IFontResolver
{
    /// <summary>
    /// Face name to embedded resource. Three files, not six: italic is simulated (nothing in these
    /// documents is italic), and Cormorant's bold is simulated too, since it appears only as
    /// headings at a size where the synthesised weight is indistinguishable.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Faces =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Manrope#Regular"] = "SoftCo.Assets.Fonts.Manrope-Regular.ttf",
            ["Manrope#SemiBold"] = "SoftCo.Assets.Fonts.Manrope-SemiBold.ttf",
            ["CormorantGaramond#Regular"] = "SoftCo.Assets.Fonts.CormorantGaramond-Regular.ttf"
        };

    private static readonly ConcurrentDictionary<string, byte[]> Cache = new();

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        // Normalised because MigraDoc passes the family name through from the document, and
        // "Cormorant Garamond" and "cormorantgaramond" must land on the same face.
        var family = new string(familyName.Where(c => !char.IsWhiteSpace(c)).ToArray());

        if (family.Equals("Manrope", StringComparison.OrdinalIgnoreCase))
            return new FontResolverInfo(
                isBold ? "Manrope#SemiBold" : "Manrope#Regular",
                mustSimulateBold: false,
                mustSimulateItalic: isItalic);

        if (family.Equals("CormorantGaramond", StringComparison.OrdinalIgnoreCase))
            return new FontResolverInfo(
                "CormorantGaramond#Regular",
                mustSimulateBold: isBold,
                mustSimulateItalic: isItalic);

        // Anything else falls back to the body face rather than returning null. A null here
        // surfaces much later as an opaque PDFsharp exception; a document that quietly renders in
        // Manrope is a far better failure than one that does not render at all.
        return new FontResolverInfo(
            isBold ? "Manrope#SemiBold" : "Manrope#Regular",
            mustSimulateBold: false,
            mustSimulateItalic: isItalic);
    }

    public byte[]? GetFont(string faceName) =>
        Cache.GetOrAdd(faceName, static name =>
        {
            if (!Faces.TryGetValue(name, out var resource))
                throw new InvalidOperationException(
                    $"No embedded font is mapped to the face '{name}'.");

            var assembly = typeof(EmbeddedFontResolver).GetTypeInfo().Assembly;
            using var stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException(
                    $"The font '{resource}' is not embedded in this assembly. It is declared as an " +
                    "EmbeddedResource in SoftCo.csproj - check the file exists under Assets/Fonts. " +
                    "Without it, PDF rendering fails everywhere rather than falling back silently.");

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        });

    /// <summary>
    /// Confirms every declared face actually resolves to bytes. Called at startup so a missing or
    /// mis-pathed font file is a loud failure then, rather than the first time somebody tries to
    /// approve a purchase order.
    /// </summary>
    public void Verify()
    {
        foreach (var face in Faces.Keys)
        {
            var bytes = GetFont(face);
            if (bytes is null || bytes.Length == 0)
                throw new InvalidOperationException($"The embedded font '{face}' is empty.");
        }
    }
}
