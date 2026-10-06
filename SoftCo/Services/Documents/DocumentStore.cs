using SoftCo.Models;

namespace SoftCo.Services.Documents;

public interface IDocumentStore
{
    Task<OrderDocument> SaveAsync(int orderId, Stream content, string displayName, UploadCheck check,
                                  DocumentKind kind, string? userId, CancellationToken ct = default);

    /// <summary>
    /// Stores a document this system produced rather than one somebody uploaded - a rendered
    /// purchase order today, invoices and credit notes later.
    /// </summary>
    Task<OrderDocument> SaveGeneratedAsync(int orderId, byte[] content, string displayName,
                                           DocumentKind kind, string? userId, CancellationToken ct = default);

    Stream OpenRead(OrderDocument document);
    bool Exists(OrderDocument document);
}

/// <summary>
/// Stores order attachments under App_Data, which sits outside wwwroot and is therefore never
/// served by the static-file middleware. Everything is handed back through an authorised
/// controller action instead, so a document cannot be reached by guessing a URL.
/// </summary>
public sealed class DocumentStore : IDocumentStore
{
    private readonly string _root;

    public DocumentStore(IWebHostEnvironment env)
    {
        _root = Path.Combine(env.ContentRootPath, "App_Data", "order-documents");
    }

    public async Task<OrderDocument> SaveAsync(int orderId, Stream content, string displayName,
        UploadCheck check, DocumentKind kind, string? userId, CancellationToken ct = default)
    {
        var document = NewDocument(orderId, kind, displayName, check.Extension, check.ContentType,
                                   content.Length, userId);

        await using var file = Create(document);
        content.Position = 0;
        await content.CopyToAsync(file, ct);

        return document;
    }

    public async Task<OrderDocument> SaveGeneratedAsync(int orderId, byte[] content, string displayName,
        DocumentKind kind, string? userId, CancellationToken ct = default)
    {
        // Generated documents are always PDFs, and the bytes come from this process rather than
        // from a request, so there is nothing to validate - but they are named, stored and read
        // back through exactly the same path as an upload, which is the point. One storage rule,
        // one traversal guard.
        var document = NewDocument(orderId, kind, displayName, ".pdf", "application/pdf",
                                   content.LongLength, userId);

        await using var file = Create(document);
        await file.WriteAsync(content, ct);

        return document;
    }

    public Stream OpenRead(OrderDocument document) =>
        File.OpenRead(PathFor(document));

    public bool Exists(OrderDocument document) =>
        File.Exists(PathFor(document));

    private static OrderDocument NewDocument(int orderId, DocumentKind kind, string displayName,
        string extension, string contentType, long size, string? userId) => new()
        {
            SupplierOrderId = orderId,
            Kind = kind,
            OriginalFileName = UploadValidator.DisplayName(displayName),
            StoredName = UploadValidator.StoredName(extension),
            ContentType = contentType,
            SizeBytes = size,
            UploadedById = userId
        };

    /// <summary>
    /// Opens the file for writing at the document's resolved path. Goes through
    /// <see cref="PathFor"/> like every read does, so the traversal guard has one implementation
    /// and both callers - uploads and generated documents - are covered by it.
    /// </summary>
    private FileStream Create(OrderDocument document)
    {
        var path = PathFor(document);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return File.Create(path);
    }

    private string FolderFor(int orderId) => Path.Combine(_root, orderId.ToString());

    /// <summary>
    /// Rebuilt from the order id and the stored GUID name only - never from anything a user
    /// supplied - and then checked to be inside the root as a belt-and-braces guard against a
    /// malformed StoredName ever reaching the database.
    /// </summary>
    private string PathFor(OrderDocument document)
    {
        var path = Path.GetFullPath(Path.Combine(FolderFor(document.SupplierOrderId), document.StoredName));
        var root = Path.GetFullPath(_root);

        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Document path resolved outside the store.");

        return path;
    }
}
