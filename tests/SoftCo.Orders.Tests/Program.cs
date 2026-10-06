using System.Text;
using SoftCo.Services;
using SoftCo.Services.Documents;

var passed = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); passed++; }

static MemoryStream Bytes(params byte[] head) => new(head);
static MemoryStream Pdf() => new(Encoding.ASCII.GetBytes("%PDF-1.4 trailing content"));

// --- PO number formatting -------------------------------------------------------------
Check(PoNumberFormat.Format(2026, 1) == "PO-2026-0001", "First order of the year");
Check(PoNumberFormat.Format(2026, 31) == "PO-2026-0031", "Backfilled seed range");
Check(PoNumberFormat.Format(2027, 1) == "PO-2027-0001", "Year rolls over in the prefix");
// Four digits is a floor, not a cap: the number must never wrap or truncate.
Check(PoNumberFormat.Format(2026, 10000) == "PO-2026-10000", "Beyond four digits keeps growing");
Check(PoNumberFormat.Format(2026, 123456) == "PO-2026-123456", "Six figures still render in full");

// --- Document numbering, shared across PO / invoice / credit note ----------------------
{
    var Fmt = SoftCo.Services.Numbering.DocumentNumberFormat.Format;
    var PO = SoftCo.Services.Numbering.DocumentNumberKind.PurchaseOrder;
    var INV = SoftCo.Services.Numbering.DocumentNumberKind.CustomerInvoice;
    var CN = SoftCo.Services.Numbering.DocumentNumberKind.CreditNote;

    Check(Fmt(INV, 2026, 1) == "INV-2026-0001", "First invoice of the year");
    Check(Fmt(CN, 2026, 1) == "CN-2026-0001", "First credit note of the year");
    Check(Fmt(PO, 2026, 1) == "PO-2026-0001", "Purchase orders keep their existing shape");

    Check(Fmt(INV, 2027, 1) == "INV-2027-0001", "Year rolls over in the prefix");
    Check(Fmt(CN, 2026, 10000) == "CN-2026-10000", "Beyond four digits keeps growing");
    Check(Fmt(INV, 2026, 123456) == "INV-2026-123456", "Six figures still render in full");

    // Three prefixes, three sequences. A credit note must never be mistakable for the invoice it
    // credits, on paper or in a filename.
    Check(Fmt(PO, 2026, 7) != Fmt(INV, 2026, 7) && Fmt(INV, 2026, 7) != Fmt(CN, 2026, 7),
          "The same sequence value reads differently per document kind");

    // PoNumberFormat is now a shim over the shared formatter. The PO checks above this block are
    // unchanged and still pass, which is the evidence that moving the logic changed nothing - this
    // asserts the two agree directly.
    Check(PoNumberFormat.Format(2026, 42) == Fmt(PO, 2026, 42),
          "PoNumberFormat and DocumentNumberFormat agree");

    // An unmapped kind must fail loudly rather than issue references prefixed with its own name.
    var unmapped = (SoftCo.Services.Numbering.DocumentNumberKind)99;
    var threw = false;
    try { SoftCo.Services.Numbering.DocumentNumberFormat.Prefix(unmapped); }
    catch (ArgumentOutOfRangeException) { threw = true; }
    Check(threw, "An unknown document kind throws rather than inventing a prefix");
}

// --- Upload: accepted types -----------------------------------------------------------
Check(UploadValidator.Check("invoice.pdf", 25, Pdf()).Ok, "A real PDF is accepted");
Check(UploadValidator.Check("INVOICE.PDF", 25, Pdf()).Ok, "Extension match is case-insensitive");
Check(UploadValidator.Check("scan.png", 8, Bytes(0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 0)).Ok, "PNG accepted");
Check(UploadValidator.Check("photo.jpg", 8, Bytes(0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0)).Ok, "JPG accepted");
Check(UploadValidator.Check("photo.jpeg", 8, Bytes(0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0)).Ok, ".jpeg accepted");

Check(UploadValidator.Check("invoice.pdf", 25, Pdf()).ContentType == "application/pdf",
      "Content type comes from the verified extension, not the browser");

// --- Upload: the checks that matter ---------------------------------------------------

// A Windows executable renamed to .pdf. Extension and declared type both claim PDF; only the
// leading bytes give it away, which is exactly why they are checked.
var disguised = UploadValidator.Check("totally-an-invoice.pdf", 8, Bytes(0x4D, 0x5A, 0x90, 0x00, 0, 0, 0, 0));
Check(!disguised.Ok, "An executable renamed .pdf is rejected");

// Allow-list, not block-list.
Check(!UploadValidator.Check("script.exe", 8, Pdf()).Ok, "Disallowed extension rejected");
Check(!UploadValidator.Check("archive.zip", 8, Pdf()).Ok, "Zip rejected even with a valid-looking body");
Check(!UploadValidator.Check("noextension", 8, Pdf()).Ok, "A file with no extension is rejected");

// Size.
Check(!UploadValidator.Check("invoice.pdf", 0, Pdf()).Ok, "Empty file rejected");
Check(!UploadValidator.Check("invoice.pdf", UploadValidator.MaxBytes + 1, Pdf()).Ok, "Oversize rejected");

// A stream already read to the end must still validate: the controller hands one over straight
// after CopyToAsync, and reading from the current position there returns nothing.
var consumed = Pdf();
consumed.Position = consumed.Length;
Check(UploadValidator.Check("invoice.pdf", consumed.Length, consumed).Ok,
      "Validates a stream positioned at the end");

// --- Stored names never derive from user input ----------------------------------------
var traversal = UploadValidator.Check(@"..\..\..\windows\system32\evil.pdf", 25, Pdf());
Check(traversal.Ok, "A traversal-shaped name is judged on its contents like any other");

var stored = UploadValidator.StoredName(traversal.Extension);
Check(!stored.Contains("..") && !stored.Contains("/") && !stored.Contains("\\"),
      "Stored name carries no path separators or traversal");
Check(stored.EndsWith(".pdf") && stored.Length == 32 + 4, "Stored name is a bare GUID plus extension");
Check(UploadValidator.StoredName(".pdf") != UploadValidator.StoredName(".pdf"), "Stored names are unique");

Check(UploadValidator.DisplayName(@"..\..\evil.pdf") == "evil.pdf", "Display name keeps only the file name");
Check(UploadValidator.DisplayName(new string('x', 400) + ".pdf").Length <= 260, "Display name is bounded");

// --- Sign-in email rules ---------------------------------------------------------------
// These exist because [EmailAddress] and Identity's username charset disagree, and a value that
// passes one and fails the other produces an error about a field the user never filled in.

static bool Accepts(string? email) => SoftCo.Validation.SignInEmailRules.Validate(email) is null;

Check(Accepts("ok@softandco.co.za"), "Ordinary address accepted");
Check(Accepts("tom+po@softandco.co.za"), "Plus addressing accepted - it is inside Identity's charset");
Check(Accepts("a.b-c_d@sub.example.co.za"), "Dots, hyphens, underscores and subdomains accepted");

// Passed [EmailAddress] before this rule existed.
Check(!Accepts("a@b"), "No top-level domain rejected");
Check(!Accepts("a b@x.com"), "Space rejected");
Check(!Accepts("x@y.c"), "Single-character suffix rejected");
Check(!Accepts("@softandco.co.za"), "Missing local part rejected");
Check(!Accepts("someone@"), "Missing domain rejected");
Check(!Accepts("two@at@x.com"), "Two at-signs rejected");
Check(!Accepts("trailing@dot."), "Empty final label rejected");

// Inside Identity's charset check, so it fails here with a message about the characters rather
// than inside CreateAsync with a message about usernames.
Check(SoftCo.Validation.SignInEmailRules.Validate("o'brien@softandco.co.za")
      == SoftCo.Validation.SignInEmailRules.CharacterMessage,
      "Apostrophe rejected with the character message, not a username error");

Check(!Accepts(new string('a', 250) + "@softandco.co.za"), "Over-length address rejected");

// [Required] owns emptiness; this rule stays quiet so the user gets one message, not two.
Check(Accepts(null) && Accepts("") && Accepts("   "), "Empty values left to [Required]");


// --- South African VAT numbers ---------------------------------------------------------
// Ten digits beginning with 4. No published checksum, so this catches a mistyped, transposed or
// truncated number - not an invented one.
{
    var Valid = SoftCo.Validation.VatNumberRules.IsValidSouthAfrican;

    Check(Valid("4123456789"), "Ten digits starting with 4 accepted");
    Check(Valid("4000000000"), "All-zero tail is still well formed");
    Check(Valid("4123 456 789"), "Spaces as pasted from a letterhead are stripped");
    Check(Valid("  4123456789  "), "Surrounding whitespace ignored");

    Check(!Valid("123456789"), "Nine digits rejected");
    Check(!Valid("41234567890"), "Eleven digits rejected");
    Check(!Valid("5123456789"), "Must begin with 4");
    Check(!Valid("412345678X"), "Non-digit rejected");
    Check(!Valid("4-12-345-678"), "Punctuation is not whitespace and is rejected");

    // A blank number is legitimate - plenty of clients are not VAT registered - and emptiness
    // belongs to [Required] where it applies, not to this rule.
    Check(Valid(null) && Valid("") && Valid("   "), "Absent number passes");

    Check(SoftCo.Validation.VatNumberRules.Normalise("  4123 456 789 ") == "4123456789",
          "Normalise strips every space");
    Check(SoftCo.Validation.VatNumberRules.Normalise("   ") is null,
          "Normalise reports absence as null, not as an empty string");

    // Country decides whether the SA format applies at all. A Mauritian registration follows its
    // own format, and applying the SARS rule to it would make exactly the zero-rated clients
    // impossible to enter.
    var IsSa = SoftCo.Validation.VatNumberRules.IsSouthAfrica;
    Check(IsSa("South Africa") && IsSa("south africa") && IsSa("SOUTH AFRICA"),
          "Country match ignores case");
    Check(IsSa(" South  Africa ") && IsSa("RSA") && IsSa("za"),
          "Spacing and the usual abbreviations accepted");
    Check(!IsSa("Mauritius") && !IsSa("United Kingdom") && !IsSa(null),
          "Anything else is treated as foreign, which only relaxes the format check");
}


// --- A stored purchase order must never read as the supplier's invoice ------------------
// HasInvoice gates the request-payment action. It tests == Invoice rather than != Other, so that
// adding DocumentKind.PurchaseOrder could not turn a document Soft & Co wrote themselves into
// permission to ask finance for money. These checks exist so a later "tidy-up" cannot regress it.
{
    var order = new SoftCo.Models.SupplierOrder();
    Check(!order.HasInvoice, "An order with no documents has no invoice");

    order.Documents.Add(new SoftCo.Models.OrderDocument { Kind = SoftCo.Models.DocumentKind.PurchaseOrder });
    Check(!order.HasInvoice, "A generated purchase order does NOT satisfy HasInvoice");

    order.Documents.Add(new SoftCo.Models.OrderDocument { Kind = SoftCo.Models.DocumentKind.Other });
    Check(!order.HasInvoice, "A packing list does not satisfy HasInvoice either");

    order.Documents.Add(new SoftCo.Models.OrderDocument { Kind = SoftCo.Models.DocumentKind.Invoice });
    Check(order.HasInvoice, "Only the supplier's invoice unlocks the payment request");
}

// --- Settlement is derived from the payments, never typed in ---------------------------
{
    SoftCo.Models.SupplierOrder Order(decimal invoiced, params decimal[] paid)
    {
        var o = new SoftCo.Models.SupplierOrder
        {
            InvoiceValueForeign = invoiced,
            InvoiceValueZar = invoiced * 2.5m,
            ExchangeRate = 2.5m
        };
        foreach (var p in paid)
            o.Payments.Add(new SoftCo.Models.OrderPayment { AmountForeign = p, AmountZar = p * 2.5m });
        return o;
    }

    Check(Order(1000m).SettlementStatus == SoftCo.Models.SettlementStatus.Unpaid, "No payments is Unpaid");
    Check(Order(1000m, 300m).SettlementStatus == SoftCo.Models.SettlementStatus.PartPaid, "A deposit is PartPaid");
    Check(Order(1000m, 300m, 700m).SettlementStatus == SoftCo.Models.SettlementStatus.Paid, "Deposit plus settlement is Paid");

    // The spreadsheet this replaces had rows reading "Outstanding" while fully paid, because the
    // status was a column somebody typed. Overpayment must still read as Paid, not drift back.
    Check(Order(1000m, 1200m).SettlementStatus == SoftCo.Models.SettlementStatus.Paid, "Overpayment reads as Paid");
    Check(Order(0m, 0m).SettlementStatus == SoftCo.Models.SettlementStatus.Unpaid, "A zero-value order is Unpaid, not Paid");

    Check(Order(1000m, 300m).OutstandingForeign == 700m, "Outstanding is invoiced minus paid");
    Check(Order(1000m, 300m).PaidZar == 750m, "Rand paid sums the Rand column, not a reconversion");
    Check(Order(1000m, 1200m).OutstandingForeign == -200m, "Overpayment shows as negative, not clamped to zero");
}

// --- The purchase-order document model --------------------------------------------------
{
    SoftCo.Services.Pdf.Models.PurchaseOrderLine Line(string ccy, decimal foreign, decimal zar) =>
        new("Lighting", "Fairmont", ccy, foreign, 2.5m, zar);

    var one = new SoftCo.Services.Pdf.Models.PurchaseOrderDocument(
        "PO-2026-0001", new DateOnly(2026, 10, 6),
        new SoftCo.Services.Pdf.Models.PdfParty("Soft & Co.", []),
        new SoftCo.Services.Pdf.Models.PdfParty("Keorh", []),
        [Line("CNY", 1000m, 2500m), Line("CNY", 500m, 1250m)]);

    Check(one.TotalZar == 3750m, "Rand total sums the lines");
    Check(one.TotalForeign is ("CNY", 1500m), "One currency across the lines totals in that currency");

    // Mixed currencies cannot be added. The document reports nothing rather than a number that
    // looks like money and is not - the same rule the project exposure figures follow.
    var mixed = one with { Lines = [Line("CNY", 1000m, 2500m), Line("USD", 100m, 1800m)] };
    Check(mixed.TotalForeign is null, "Mixed currencies produce no foreign total");
    Check(mixed.TotalZar == 4300m, "Rand still totals across mixed currencies");

    var empty = one with { Lines = [] };
    Check(empty.TotalForeign is null && empty.TotalZar == 0m, "An empty document totals to nothing, not a crash");
}

Console.WriteLine($"All {passed} checks passed.");
