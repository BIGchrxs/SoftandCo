using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using SoftCo.Services.Pdf.Models;

namespace SoftCo.Services.Pdf;

/// <summary>
/// Renders documents with MigraDoc on top of PDFsharp.
///
/// The only file in the system that references either library. Everything else works against
/// <see cref="IPdfRenderer"/> and the plain records in <c>Services.Pdf.Models</c>, so replacing the
/// library is a matter of writing another class beside this one.
///
/// MigraDoc is a document model - sections, paragraphs, tables - rather than a drawing API, so the
/// layout here is described rather than positioned. That is what keeps a long supplier name or a
/// three-line address from colliding with the column beside it.
/// </summary>
public sealed class MigraDocPdfRenderer : IPdfRenderer
{
    public byte[] RenderPurchaseOrder(PurchaseOrderDocument po)
    {
        var doc = NewDocument($"Purchase order {po.PoNumber}", po.From.Name);
        var section = doc.LastSection;

        Letterhead(section, po.From);
        Title(section, "Purchase order", po.PoNumber);
        Parties(section, po);
        Lines(section, po);
        Totals(section, po);
        Approval(section, po);
        Notes(section, po);

        var renderer = new PdfDocumentRenderer { Document = doc };
        renderer.RenderDocument();

        using var buffer = new MemoryStream();
        renderer.PdfDocument.Save(buffer, closeStream: false);
        return buffer.ToArray();
    }

    // --- Document shell ---------------------------------------------------------------------

    private static Document NewDocument(string title, string author)
    {
        var doc = new Document { Info = { Title = title, Author = author } };

        var normal = doc.Styles[StyleNames.Normal]!;
        normal.Font.Name = PdfBrand.SansFont;
        normal.Font.Size = PdfBrand.BodySizePt;
        normal.Font.Color = Hex(PdfBrand.Ink);
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(4);

        var section = doc.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.TopMargin = Unit.FromCentimeter(PdfBrand.PageMarginCm);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(PdfBrand.PageMarginCm);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(PdfBrand.PageMarginCm);
        section.PageSetup.RightMargin = Unit.FromCentimeter(PdfBrand.PageMarginCm);

        Footer(section, title);
        return doc;
    }

    private static void Footer(Section section, string title)
    {
        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Font.Size = PdfBrand.LabelSizePt;
        footer.Format.Font.Color = Hex(PdfBrand.InkSoft);
        footer.Format.Alignment = ParagraphAlignment.Center;
        footer.AddText(title + "   ·   page ");
        footer.AddPageField();
        footer.AddText(" of ");
        footer.AddNumPagesField();
    }

    // --- Blocks -------------------------------------------------------------------------------

    private static void Letterhead(Section section, PdfParty from)
    {
        var name = section.AddParagraph(from.Name);
        name.Format.Font.Name = PdfBrand.DisplayFont;
        name.Format.Font.Size = 20;
        name.Format.Font.Color = Hex(PdfBrand.Accent);
        name.Format.SpaceAfter = Unit.FromPoint(2);

        var detail = section.AddParagraph();
        detail.Format.Font.Size = PdfBrand.LabelSizePt;
        detail.Format.Font.Color = Hex(PdfBrand.InkSoft);

        foreach (var line in from.AddressLines) detail.AddText(line + "   ");
        if (from.RegistrationNumber is { Length: > 0 } reg) detail.AddText($"   Reg {reg}");
        if (from.VatNumber is { Length: > 0 } vat) detail.AddText($"   VAT {vat}");

        Rule(section, Hex(PdfBrand.Accent), Unit.FromPoint(1.2));
    }

    private static void Title(Section section, string title, string reference)
    {
        var p = section.AddParagraph();
        p.Format.SpaceBefore = Unit.FromPoint(10);
        p.Format.SpaceAfter = Unit.FromPoint(2);

        var heading = p.AddFormattedText(title.ToUpperInvariant());
        heading.Font.Name = PdfBrand.SansFont;
        heading.Font.Size = PdfBrand.LabelSizePt;
        heading.Font.Color = Hex(PdfBrand.InkSoft);

        var number = section.AddParagraph(reference);
        number.Format.Font.Size = PdfBrand.TitleSizePt;
        number.Format.Font.Color = Hex(PdfBrand.AccentStrong);
        number.Format.SpaceAfter = Unit.FromPoint(10);
    }

    /// <summary>
    /// Supplier on the left, the order's own facts on the right. A two-column borderless table
    /// rather than tabs, so a four-line address pushes nothing sideways.
    /// </summary>
    private static void Parties(Section section, PurchaseOrderDocument po)
    {
        var table = Grid(section, [9.0, 7.5]);
        var row = table.AddRow();

        var supplier = row.Cells[0].AddParagraph();
        Label(supplier, "Supplier");
        supplier.AddLineBreak();
        var supplierName = supplier.AddFormattedText(po.To.Name);
        supplierName.Bold = true;

        foreach (var line in po.To.AddressLines)
        {
            supplier.AddLineBreak();
            supplier.AddText(line);
        }
        if (po.To.ContactName is { Length: > 0 } c) { supplier.AddLineBreak(); supplier.AddText(c); }
        if (po.To.ContactEmail is { Length: > 0 } e) { supplier.AddLineBreak(); supplier.AddText(e); }

        var facts = row.Cells[1].AddParagraph();
        Fact(facts, "Date issued", Date(po.IssuedOn), first: true);
        if (po.CargoReadinessDate is DateOnly ready)
            Fact(facts, "Cargo readiness", Date(ready));
        if (po.SupplierInvoiceRef is { Length: > 0 } inv)
            Fact(facts, "Supplier invoice ref", inv);
        if (po.PreparedBy is { Length: > 0 } by)
            Fact(facts, "Prepared by", by);

        section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(8);
    }

    private static void Lines(Section section, PurchaseOrderDocument po)
    {
        var table = Grid(section, [6.5, 3.2, 2.2, 2.3, 2.3]);
        table.Borders.Color = Hex(PdfBrand.Rule);

        var head = table.AddRow();
        head.Shading.Color = Hex(PdfBrand.BeigeDeep);
        head.Borders.Bottom.Width = 0.75;
        head.Borders.Bottom.Color = Hex(PdfBrand.Accent);
        head.TopPadding = Unit.FromPoint(5);
        head.BottomPadding = Unit.FromPoint(5);

        HeaderCell(head.Cells[0], "Description");
        HeaderCell(head.Cells[1], "Project");
        HeaderCell(head.Cells[2], "Rate", right: true);
        HeaderCell(head.Cells[3], "Amount", right: true);
        HeaderCell(head.Cells[4], "ZAR", right: true);

        foreach (var line in po.Lines)
        {
            var row = table.AddRow();
            row.TopPadding = Unit.FromPoint(4);
            row.BottomPadding = Unit.FromPoint(4);
            row.Borders.Bottom.Width = 0.25;
            row.Borders.Bottom.Color = Hex(PdfBrand.Rule);

            row.Cells[0].AddParagraph(line.Description);
            row.Cells[1].AddParagraph(line.Projects ?? "—").Format.Font.Color = Hex(PdfBrand.InkSoft);

            // Rate to six places, matching the numeric(18,6) the value is stored at: a rate
            // rounded for display is the first step towards a total nobody can reproduce.
            Number(row.Cells[2], Amount(line.ExchangeRate, "N6"));
            Number(row.Cells[3], $"{line.CurrencyCode} {Amount(line.AmountForeign)}");
            Number(row.Cells[4], Amount(line.AmountZar));
        }
    }

    private static void Totals(Section section, PurchaseOrderDocument po)
    {
        var table = Grid(section, [11.9, 4.6]);
        var row = table.AddRow();
        row.TopPadding = Unit.FromPoint(6);

        var label = row.Cells[0].AddParagraph();
        label.Format.Alignment = ParagraphAlignment.Right;
        Label(label, "Total to approve");

        var total = row.Cells[1].AddParagraph($"R {Amount(po.TotalZar)}");
        total.Format.Alignment = ParagraphAlignment.Right;
        total.Format.Font.Size = 13;
        total.Format.Font.Bold = true;
        total.Format.Font.Color = Hex(PdfBrand.AccentStrong);

        // The foreign figure is secondary and only shown when it means something: mixed currencies
        // cannot be summed, so TotalForeign reports null rather than adding CNY to USD.
        if (po.TotalForeign is ({ } currency, var amount))
        {
            var foreignRow = table.AddRow();
            var foreign = foreignRow.Cells[1].AddParagraph($"{currency} {Amount(amount)}");
            foreign.Format.Alignment = ParagraphAlignment.Right;
            foreign.Format.Font.Size = PdfBrand.LabelSizePt;
            foreign.Format.Font.Color = Hex(PdfBrand.InkSoft);
        }
    }

    /// <summary>
    /// The reason this document exists. It goes to the Financial Director, so it ends with the
    /// space their decision is recorded in - filled in by the approval workflow, signed by hand if
    /// the PDF is printed.
    /// </summary>
    private static void Approval(Section section, PurchaseOrderDocument po)
    {
        section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(14);
        Rule(section, Hex(PdfBrand.Rule), Unit.FromPoint(0.5));

        var intro = section.AddParagraph();
        intro.Format.SpaceBefore = Unit.FromPoint(8);
        Label(intro, "Financial Director approval");

        var note = section.AddParagraph(
            $"Approval commits Soft & Co to R {Amount(po.TotalZar)} with {po.To.Name}. " +
            "This purchase order is not an instruction to the supplier until it has been approved and issued.");
        note.Format.Font.Size = PdfBrand.LabelSizePt;
        note.Format.Font.Color = Hex(PdfBrand.InkSoft);
        note.Format.SpaceAfter = Unit.FromPoint(16);

        var table = Grid(section, [8.25, 8.25]);
        var row = table.AddRow();
        SignatureLine(row.Cells[0], "Signature");
        SignatureLine(row.Cells[1], "Date");
    }

    private static void Notes(Section section, PurchaseOrderDocument po)
    {
        if (po.Notes is not { Length: > 0 } notes) return;

        var heading = section.AddParagraph();
        heading.Format.SpaceBefore = Unit.FromPoint(14);
        Label(heading, "Notes");

        var body = section.AddParagraph(notes);
        body.Format.Font.Size = PdfBrand.LabelSizePt;
        body.Format.Font.Color = Hex(PdfBrand.InkSoft);
    }

    // --- Small shared pieces ------------------------------------------------------------------

    private static Table Grid(Section section, double[] widthsCm)
    {
        var table = section.AddTable();
        table.Borders.Width = 0;
        foreach (var w in widthsCm) table.AddColumn(Unit.FromCentimeter(w));
        return table;
    }

    private static void HeaderCell(Cell cell, string text, bool right = false)
    {
        var p = cell.AddParagraph();
        p.Format.Alignment = right ? ParagraphAlignment.Right : ParagraphAlignment.Left;
        cell.VerticalAlignment = VerticalAlignment.Center;
        Label(p, text);
    }

    /// <summary>The deck's micro-label: small, uppercase, widely tracked, never bold.</summary>
    private static void Label(Paragraph p, string text)
    {
        var t = p.AddFormattedText(text.ToUpperInvariant());
        t.Font.Size = PdfBrand.LabelSizePt;
        t.Font.Color = Hex(PdfBrand.InkSoft);
    }

    private static void Fact(Paragraph p, string label, string value, bool first = false)
    {
        if (!first) p.AddLineBreak();
        var l = p.AddFormattedText(label.ToUpperInvariant() + "  ");
        l.Font.Size = PdfBrand.LabelSizePt;
        l.Font.Color = Hex(PdfBrand.InkSoft);
        p.AddText(value);
    }

    private static void Number(Cell cell, string text)
    {
        var p = cell.AddParagraph(text);
        p.Format.Alignment = ParagraphAlignment.Right;
        // Figures never use the display face - the same rule site.css applies with --sc-font-num.
        p.Format.Font.Name = PdfBrand.SansFont;
    }

    private static void SignatureLine(Cell cell, string label)
    {
        var line = cell.AddParagraph();
        line.Format.Borders.Bottom.Width = 0.5;
        line.Format.Borders.Bottom.Color = Hex(PdfBrand.Ink);
        line.Format.SpaceAfter = Unit.FromPoint(3);
        line.AddText(" ");

        var caption = cell.AddParagraph();
        Label(caption, label);
    }

    private static void Rule(Section section, Color color, Unit width)
    {
        var p = section.AddParagraph();
        p.Format.Borders.Bottom.Width = width;
        p.Format.Borders.Bottom.Color = color;
        p.Format.SpaceBefore = Unit.FromPoint(4);
        p.Format.SpaceAfter = 0;
    }

    /// <summary>
    /// Every number and date on a document is formatted against a fixed culture, never the ambient
    /// one.
    ///
    /// Program.cs pins InvariantCulture for the request pipeline, so inside a web request this
    /// would mostly come out right by accident. It is not left to accident: a machine set to a
    /// comma-decimal locale renders R 318 622,26 instead of R 318,622.26, and the first time that
    /// bites will be a background job, a scheduled regeneration or a container with a different
    /// LANG - somewhere no request culture applies. An issued document is a financial record and
    /// must read identically wherever it was produced.
    /// </summary>
    private static readonly CultureInfo DocumentCulture = CultureInfo.InvariantCulture;

    private static string Amount(decimal value, string format = "N2") =>
        value.ToString(format, DocumentCulture);

    private static string Date(DateOnly value) => value.ToString("dd MMM yyyy", DocumentCulture);

    /// <summary>
    /// The one place the brand's hex strings become MigraDoc colours, so PdfBrand stays free of any
    /// library type.
    /// </summary>
    private static Color Hex(string value) => Color.Parse(value);
}
