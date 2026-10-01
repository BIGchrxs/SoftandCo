using SoftCo.Models;

namespace SoftCo.Services.Documents;

public interface IDocumentStore
{
    Task<OrderDocument> SaveAsync(int orderId, Stream content, string displayName, UploadCheck check,
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
        var folder = FolderFor(orderId);
        Directory.CreateDirectory(folder);

        var stored = UploadValidator.StoredName(check.Extension);
        var path = Path.Combine(folder, stored);

        await using (var file = File.Create(path))
        {
            content.Position = 0;
            await content.CopyToAsync(file, ct);
        }

        return new OrderDocument
        {
            SupplierOrderId = orderId,
            Kind = kind,
            OriginalFileName = UploadValidator.DisplayName(displayName),
            StoredName = stored,
            ContentType = check.ContentType,
            SizeBytes = content.Length,
            UploadedById = userId
        };
    }

    public Stream OpenRead(OrderDocument document) =>
        File.OpenRead(PathFor(document));

    public bool Exists(OrderDocument document) =>
        File.Exists(PathFor(document));

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
