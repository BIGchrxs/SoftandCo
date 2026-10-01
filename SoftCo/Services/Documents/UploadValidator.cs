namespace SoftCo.Services.Documents;

public sealed record UploadCheck(bool Ok, string? Error, string ContentType, string Extension)
{
    public static UploadCheck Fail(string error) => new(false, error, "", "");
}

/// <summary>
/// Decides whether an uploaded file may be stored.
///
/// Three rules, in order of how often they are got wrong:
///
/// 1. The extension is checked against an allow-list, never a block-list. A block-list is a bet
///    that you can name every dangerous extension, and that bet is always lost eventually.
/// 2. The declared content type must agree with the extension AND with the file's leading bytes.
///    The browser-supplied content type is attacker-controlled; the magic bytes are not, so a
///    payload.exe renamed to invoice.pdf is rejected here rather than stored and served back.
/// 3. The size is capped. The controller also sets RequestSizeLimit, because a check that only
///    runs after the whole body is buffered is not a defence against someone sending 2 GB.
///
/// Pure and static so the whole thing is unit-testable without a web request.
/// </summary>
public static class UploadValidator
{
    public const long MaxBytes = 10 * 1024 * 1024;

    private static readonly Dictionary<string, string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg"
    };

    public static UploadCheck Check(string? fileName, long length, Stream content)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return UploadCheck.Fail("No file was supplied.");

        if (length <= 0)
            return UploadCheck.Fail("That file is empty.");

        if (length > MaxBytes)
            return UploadCheck.Fail($"Files must be {MaxBytes / 1024 / 1024} MB or smaller.");

        // Take only the final extension, from the name's last segment. Path.GetExtension on a
        // crafted name like "..\\..\\evil.pdf" still yields ".pdf"; the name is never used to
        // build a path anyway, but this keeps the check honest.
        var ext = Path.GetExtension(Path.GetFileName(fileName));

        if (string.IsNullOrWhiteSpace(ext) || !Allowed.TryGetValue(ext, out var expectedType))
            return UploadCheck.Fail("Only PDF, PNG and JPG files can be attached.");

        if (!HasMatchingSignature(content, expectedType))
            return UploadCheck.Fail("That file's contents do not match its extension.");

        return new UploadCheck(true, null, expectedType, ext.ToLowerInvariant());
    }

    /// <summary>
    /// Reads the first bytes of the file and restores the caller's position.
    ///
    /// Seeks to 0 explicitly rather than reading from wherever the stream happens to be: a
    /// caller that has just finished CopyToAsync leaves the position at the end, where a read
    /// returns nothing and every upload would be rejected as mismatched.
    /// </summary>
    private static bool HasMatchingSignature(Stream content, string expectedType)
    {
        if (!content.CanSeek) return false;

        var origin = content.Position;
        content.Position = 0;
        Span<byte> head = stackalloc byte[8];
        var read = content.Read(head);
        content.Position = origin;

        if (read < 4) return false;

        return expectedType switch
        {
            // "%PDF"
            "application/pdf" => head[0] == 0x25 && head[1] == 0x50 && head[2] == 0x44 && head[3] == 0x46,
            // \x89 P N G
            "image/png" => head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47,
            // JFIF/Exif start of image
            "image/jpeg" => head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF,
            _ => false
        };
    }

    /// <summary>
    /// The only name that ever reaches the filesystem. A GUID, so nothing an uploader controls
    /// influences where the bytes land.
    /// </summary>
    public static string StoredName(string extension) => $"{Guid.NewGuid():N}{extension}";

    /// <summary>Trims a display name to something safe to render and store.</summary>
    public static string DisplayName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        return name.Length > 260 ? name[^260..] : name;
    }
}
