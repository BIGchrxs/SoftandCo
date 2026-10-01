using System.ComponentModel.DataAnnotations;

namespace SoftCo.Models;

/// <summary>
/// A file attached to an order - typically the supplier's invoice, sometimes a packing list or
/// proof of delivery.
///
/// The bytes live outside wwwroot under App_Data, named by a server-generated GUID. The name the
/// browser supplied is kept in <see cref="OriginalFileName"/> for display only and is never used
/// to build a path, so a crafted filename cannot escape the storage directory or land somewhere
/// the web server would execute it.
/// </summary>
public class OrderDocument
{
    public int Id { get; set; }

    public int SupplierOrderId { get; set; }
    public SupplierOrder? SupplierOrder { get; set; }

    public DocumentKind Kind { get; set; } = DocumentKind.Invoice;

    /// <summary>Display only. Never trusted, never used as a path component.</summary>
    [Required, StringLength(260)]
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>Server-generated "{guid}{ext}". This is the only name touching the filesystem.</summary>
    [Required, StringLength(100)]
    public string StoredName { get; set; } = string.Empty;

    /// <summary>Verified against the file's magic bytes at upload, not taken from the browser.</summary>
    [Required, StringLength(100)]
    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    [StringLength(450)] public string? UploadedById { get; set; }
}
