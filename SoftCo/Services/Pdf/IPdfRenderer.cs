using SoftCo.Services.Pdf.Models;

namespace SoftCo.Services.Pdf;

/// <summary>
/// Turns a document model into PDF bytes.
///
/// The seam. Everything upstream - the services that build documents, the controllers that offer
/// them, the email that attaches them - depends on this interface and on the plain records in
/// <c>Services.Pdf.Models</c>. Swapping the PDF library means writing one new class behind this
/// interface and changing nothing else, which matters more than usual here: PDFsharp is MIT today,
/// and the alternatives that were rejected carry revenue tests and AGPL terms that could force a
/// change later.
///
/// A method per document kind rather than one generic Render: the compiler then says which
/// documents a renderer actually supports, instead of a runtime failure on an unhandled type.
/// </summary>
public interface IPdfRenderer
{
    byte[] RenderPurchaseOrder(PurchaseOrderDocument document);

    byte[] RenderCustomerInvoice(CustomerInvoiceDocument document);
}
