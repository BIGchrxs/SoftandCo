using System.Text;
using SoftCo.Models;
using SoftCo.Services.Approvals;
using SoftCo.Services.Invoicing;
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


// --- Who may approve what ---------------------------------------------------------------
{
    // Submitting: the Financial Director is being asked to commit money to a supplier for a
    // project. An order missing any of the three is not a decision anyone can make.
    Check(ApprovalRules.CanSubmit(PoApprovalStatus.Draft, true, 1000m, true).Ok, "A complete draft can be submitted");
    Check(ApprovalRules.CanSubmit(PoApprovalStatus.Rejected, true, 1000m, true).Ok, "A rejected order can be resubmitted");
    Check(!ApprovalRules.CanSubmit(PoApprovalStatus.Draft, false, 1000m, true).Ok, "No supplier, no submission");
    Check(!ApprovalRules.CanSubmit(PoApprovalStatus.Draft, true, 0m, true).Ok, "No value, no submission");
    Check(!ApprovalRules.CanSubmit(PoApprovalStatus.Draft, true, -5m, true).Ok, "A negative value is not a value");
    Check(!ApprovalRules.CanSubmit(PoApprovalStatus.Draft, true, 1000m, false).Ok, "No project, no submission");
    Check(!ApprovalRules.CanSubmit(PoApprovalStatus.PendingApproval, true, 1000m, true).Ok, "Cannot submit twice");
    Check(!ApprovalRules.CanSubmit(PoApprovalStatus.Issued, true, 1000m, true).Ok, "Cannot resubmit an issued order");

    // The rule the whole phase exists for.
    Check(ApprovalRules.CanDecide(ApprovalStatus.Pending, "user-a", "user-b").Ok, "Someone else may decide");
    Check(!ApprovalRules.CanDecide(ApprovalStatus.Pending, "user-a", "user-a").Ok, "You cannot approve your own submission");
    Check(!ApprovalRules.CanDecide(ApprovalStatus.Pending, "User-A", "user-a").Ok, "Self-approval check ignores case");
    Check(!ApprovalRules.CanDecide(ApprovalStatus.Pending, "user-a", null).Ok, "An unattributable decision is refused");
    Check(!ApprovalRules.CanDecide(ApprovalStatus.Approved, "user-a", "user-b").Ok, "Already decided, not decided again");
    Check(!ApprovalRules.CanDecide(ApprovalStatus.Rejected, "user-a", "user-b").Ok, "A rejected request is closed");

    // A rejection that says nothing sends work back to someone who has no idea what to change.
    Check(ApprovalRules.CanReject(ApprovalStatus.Pending, "a", "b", "Price too high, renegotiate").Ok, "A real reason passes");
    Check(!ApprovalRules.CanReject(ApprovalStatus.Pending, "a", "b", "no").Ok, "A one-word rejection is refused");
    Check(!ApprovalRules.CanReject(ApprovalStatus.Pending, "a", "b", "          ").Ok, "Whitespace is not a reason");
    Check(!ApprovalRules.CanReject(ApprovalStatus.Pending, "a", "b", null).Ok, "No reason at all is refused");
    Check(!ApprovalRules.CanReject(ApprovalStatus.Pending, "a", "a", "Price too high, renegotiate").Ok,
          "Self-rejection is refused even with a good reason");

    Check(ApprovalRules.CanWithdraw(PoApprovalStatus.PendingApproval).Ok, "A waiting request can be withdrawn");
    Check(!ApprovalRules.CanWithdraw(PoApprovalStatus.Approved).Ok, "Withdrawing cannot undo a decision already made");
    Check(!ApprovalRules.CanWithdraw(PoApprovalStatus.Draft).Ok, "Nothing to withdraw from a draft");

    Check(ApprovalRules.EditInvalidates(PoApprovalStatus.Approved), "Editing an approved order invalidates it");
    Check(ApprovalRules.EditInvalidates(PoApprovalStatus.PendingApproval), "Editing a waiting order invalidates it");
    // Issued invalidates too. Leaving it Issued after a material change would show Financial
    // Director approval for an amount nobody approved.
    Check(ApprovalRules.EditInvalidates(PoApprovalStatus.Issued), "Editing an issued order invalidates it as well");
    Check(!ApprovalRules.EditInvalidates(PoApprovalStatus.Rejected), "A rejected order is already back with its submitter");
    Check(!ApprovalRules.EditInvalidates(PoApprovalStatus.Draft), "A draft has nothing to lose");
}

// --- The fingerprint: an order edited after approval cannot be issued --------------------
{
    string Fp(int supplier = 1, string ccy = "CNY", decimal rate = 2.4815m,
              decimal foreign = 128400m, decimal zar = 318622.26m, int[]? projects = null) =>
        SubjectFingerprint.ForPurchaseOrder(supplier, ccy, rate, foreign, zar, projects ?? [3, 11]);

    var original = Fp();

    Check(original.Length == 64, "A fingerprint is a 64-character SHA-256 hex string");
    Check(Fp() == original, "The same facts hash the same way twice");

    // Project order and duplicates are not material; the set of projects is.
    Check(Fp(projects: [11, 3]) == original, "Project order does not change the fingerprint");
    Check(Fp(projects: [3, 11, 3]) == original, "A duplicated project does not change it either");
    Check(Fp(projects: [3]) != original, "Removing a project does change it");

    // Everything that changes what Soft & Co owes, or to whom.
    Check(Fp(supplier: 2) != original, "A different supplier is a different commitment");
    Check(Fp(ccy: "USD") != original, "A different currency is a different commitment");
    Check(Fp(rate: 2.4816m) != original, "A changed exchange rate invalidates approval");
    Check(Fp(foreign: 128401m) != original, "A changed foreign value invalidates approval");
    Check(Fp(zar: 318622.27m) != original, "A single cent on the Rand value invalidates approval");

    Check(Fp(ccy: "cny") == original, "Currency case is normalised, not treated as a change");

    // Culture: a comma-decimal machine must not hash differently from a dot-decimal one, or every
    // approval would invalidate itself the moment it was checked on another host.
    Check(SubjectFingerprint.CanonicalForm(1, "CNY", 2.4815m, 128400m, 318622.26m, [3, 11])
            .Contains("2.481500"),
          "The canonical form is written with an invariant decimal point");

    Check(SubjectFingerprint.Matches(original, Fp()), "A matching fingerprint is recognised");
    Check(!SubjectFingerprint.Matches(original, Fp(zar: 1m)), "A mismatch is detected");

    // An approval that cannot prove what it covered must not be treated as covering anything.
    Check(!SubjectFingerprint.Matches(null, original), "A missing stored fingerprint never matches");
    Check(!SubjectFingerprint.Matches("", original), "An empty stored fingerprint never matches");

    // Issuing.
    Check(ApprovalRules.CanIssue(PoApprovalStatus.Approved, original, original).Ok, "An unchanged approved order issues");
    Check(!ApprovalRules.CanIssue(PoApprovalStatus.Approved, original, Fp(zar: 400000m)).Ok,
          "An order edited after approval cannot be issued");
    Check(!ApprovalRules.CanIssue(PoApprovalStatus.Draft, original, original).Ok, "A draft cannot be issued");
    Check(!ApprovalRules.CanIssue(PoApprovalStatus.PendingApproval, original, original).Ok, "A waiting order cannot be issued");
    Check(!ApprovalRules.CanIssue(PoApprovalStatus.Issued, original, original).Ok, "An issued order is not issued twice");
    Check(!ApprovalRules.CanIssue(PoApprovalStatus.Approved, null, original).Ok,
          "Approved with no recorded fingerprint cannot be issued");
}


// --- Payment release: raise, approve, release -------------------------------------------
{
    // Raising. The invoice gate is the one the old direct-send flow had; what is new is that
    // passing it no longer sends anything.
    Check(ApprovalRules.CanRaiseRelease(true, 1000m, true, false).Ok, "A complete request can be raised");
    Check(!ApprovalRules.CanRaiseRelease(false, 1000m, true, false).Ok, "No invoice attached, nothing to pay against");
    Check(!ApprovalRules.CanRaiseRelease(true, 1000m, false, false).Ok, "No contact chosen, nobody to ask");
    Check(!ApprovalRules.CanRaiseRelease(true, 0m, true, false).Ok, "Nothing outstanding, nothing to release");
    Check(!ApprovalRules.CanRaiseRelease(true, -5m, true, false).Ok, "An overpaid order has nothing to release");
    Check(!ApprovalRules.CanRaiseRelease(true, 1000m, true, true).Ok, "One waiting request at a time");

    // Releasing. The amount is checked again against what is outstanding NOW: the Financial
    // Director approved a figure, and if a payment landed in between, that figure is no longer
    // what would be asked for.
    Check(ApprovalRules.CanRelease(PaymentReleaseStatus.Approved, 1000m, 1000m).Ok,
          "An approved release for the unchanged amount can be sent");
    Check(!ApprovalRules.CanRelease(PaymentReleaseStatus.Approved, 1000m, 700m).Ok,
          "A payment recorded after approval blocks the release");
    Check(!ApprovalRules.CanRelease(PaymentReleaseStatus.Approved, 1000m, 1000.01m).Ok,
          "One cent of difference is still a different amount");
    Check(!ApprovalRules.CanRelease(PaymentReleaseStatus.PendingApproval, 1000m, 1000m).Ok,
          "Nothing is sent before the Financial Director has agreed");
    Check(!ApprovalRules.CanRelease(PaymentReleaseStatus.Rejected, 1000m, 1000m).Ok,
          "A rejected release is not sent");
    Check(!ApprovalRules.CanRelease(PaymentReleaseStatus.Released, 1000m, 1000m).Ok,
          "A released request is not sent twice");
    Check(!ApprovalRules.CanRelease(PaymentReleaseStatus.Withdrawn, 1000m, 1000m).Ok,
          "A withdrawn release is not sent");

    Check(ApprovalRules.CanWithdrawRelease(PaymentReleaseStatus.PendingApproval).Ok,
          "A waiting release can be withdrawn");
    Check(!ApprovalRules.CanWithdrawRelease(PaymentReleaseStatus.Approved).Ok,
          "Withdrawing cannot undo an approval already given");
    Check(!ApprovalRules.CanWithdrawRelease(PaymentReleaseStatus.Released).Ok,
          "A sent request cannot be withdrawn");

    // The two statuses answer different questions and must not be conflated. A new request has
    // not been sent, and saying so is not the same as saying it failed.
    var fresh = new PaymentRequest();
    Check(fresh.Status == PaymentRequestStatus.NotSent, "A newly raised request has not been sent");
    Check(fresh.ReleaseStatus == PaymentReleaseStatus.PendingApproval, "and is waiting on the Financial Director");
    Check(fresh.IsAwaitingApproval && !fresh.IsReadyToRelease, "Awaiting approval is not ready to release");
    Check(fresh.SentAt is null, "Nothing claims to have been sent before it was");

    var approved = new PaymentRequest { ReleaseStatus = PaymentReleaseStatus.Approved };
    Check(approved.IsReadyToRelease && !approved.IsAwaitingApproval, "Approved is ready to release");

    // A released request whose email bounced is both Released and Failed - the point of keeping
    // the two apart.
    var failed = new PaymentRequest
    {
        ReleaseStatus = PaymentReleaseStatus.Released,
        Status = PaymentRequestStatus.Failed
    };
    Check(!failed.IsAwaitingApproval && !failed.IsReadyToRelease,
          "A released request that failed to send is neither waiting nor ready");
}


// --- Invoice arithmetic -----------------------------------------------------------------
// Written before the implementation. The maths is the deliverable here: every figure a client
// sees, and every figure SARS sees, comes out of this.
{
    // Rounding is away from zero, NOT the .NET default. Math.Round(decimal, int) uses banker's
    // rounding, which rounds a half to the nearest even digit - so 0.125 becomes 0.12 and the
    // shaved cent lands in the taxpayer's favour, consistently, across every line of every
    // invoice. SARS will not thank anyone for that.
    Check(InvoiceMath.Round(0.125m) == 0.13m, "A half rounds away from zero, not to even");
    Check(InvoiceMath.Round(0.145m) == 0.15m, "0.145 rounds up, where banker's rounding gives 0.14");
    Check(InvoiceMath.Round(0.135m) == 0.14m, "0.135 rounds up");
    Check(InvoiceMath.Round(-0.125m) == -0.13m, "Away from zero works downwards too");
    Check(InvoiceMath.Round(1.004m) == 1.00m, "Below the half still rounds down");
    Check(InvoiceMath.Round(10m) == 10.00m, "A whole number is unchanged");

    // Line net: quantity times unit price, rounded once.
    Check(InvoiceMath.Net(3m, 150.00m) == 450.00m, "Three at 150 is 450");
    Check(InvoiceMath.Net(2.5m, 19.99m) == 49.98m, "49.975 rounds away from zero to 49.98");
    Check(InvoiceMath.Net(0m, 100m) == 0m, "Nothing ordered is nothing owed");

    // VAT is computed per line and rounded there.
    Check(InvoiceMath.Vat(100.00m, 15m) == 15.00m, "15% of 100 is 15");
    Check(InvoiceMath.Vat(1000.00m, 15m) == 150.00m, "15% of 1000 is 150");
    Check(InvoiceMath.Vat(100.00m, 0m) == 0m, "Zero-rated lines carry no VAT");
    Check(InvoiceMath.Vat(0.10m, 15m) == 0.02m, "0.015 rounds away from zero to 0.02");

    // The rate is never assumed. 14% was the rate until 2018 and a 2025 rise was tabled and
    // withdrawn; an invoice issued at 14% must still re-render at 14% forever.
    Check(InvoiceMath.Vat(100.00m, 14m) == 14.00m, "A historical 14% line is computed at 14%");

    // Interior clients are quoted VAT-inclusive, so the entry form accepts inclusive prices and
    // back-calculates once, at save. Exclusive is what gets stored, because deriving excl from a
    // 2dp inclusive price repeatedly is lossy.
    Check(InvoiceMath.ExclusiveFromInclusive(115.00m, 15m) == 100.00m, "115 inclusive is 100 exclusive");
    Check(InvoiceMath.ExclusiveFromInclusive(11.50m, 15m) == 10.00m, "11.50 inclusive is 10.00 exclusive");
    Check(InvoiceMath.ExclusiveFromInclusive(100.00m, 0m) == 100.00m, "Zero-rated: inclusive and exclusive agree");

    // Round trip: excl -> vat -> incl -> excl must land back where it started.
    foreach (var incl in new[] { 115.00m, 1150.00m, 57.50m, 9999.99m })
    {
        var excl = InvoiceMath.ExclusiveFromInclusive(incl, 15m);
        var back = excl + InvoiceMath.Vat(excl, 15m);
        Check(back == incl, $"Inclusive {incl} survives the round trip");
    }

    // A whole line in one call.
    var line = InvoiceMath.Line(2m, 500.00m, 15m);
    Check(line.NetExclVat == 1000.00m && line.VatAmount == 150.00m && line.TotalInclVat == 1150.00m,
          "A line reports net, VAT and total consistently");

    // VAT is summed from the lines, never computed on the subtotal. Three lines of 0.10 at 15%
    // round to 0.02 each - 0.06 - while 15% of the 0.30 subtotal is 0.05. The header must agree
    // with the rows the client can see and add up themselves; that mismatch is the single most
    // common invoice complaint there is.
    var pennies = new[] { InvoiceMath.Line(1m, 0.10m, 15m), InvoiceMath.Line(1m, 0.10m, 15m), InvoiceMath.Line(1m, 0.10m, 15m) };
    var pennyTotals = InvoiceMath.Totals(pennies);
    Check(pennyTotals.VatTotal == 0.06m, "VAT sums the per-line amounts, not 15% of the subtotal");
    Check(InvoiceMath.Vat(0.30m, 15m) == 0.05m, "and 15% of the subtotal really would differ");
    Check(pennyTotals.NetTotal == 0.30m && pennyTotals.GrandTotal == 0.36m, "Totals stay internally consistent");

    // A mixed invoice. T.M Mauritius makes this concrete: goods leaving South Africa are
    // zero-rated while local delivery on the same job is standard-rated, so one invoice carries
    // both and a single header rate could not represent it.
    var mixed = InvoiceMath.Totals([
        InvoiceMath.Line(1m, 1000.00m, 15m),   // local delivery
        InvoiceMath.Line(1m, 500.00m, 0m)      // exported goods
    ]);
    Check(mixed.NetTotal == 1500.00m, "Mixed invoice nets to 1500");
    Check(mixed.VatTotal == 150.00m, "VAT applies only to the standard-rated line");
    Check(mixed.GrandTotal == 1650.00m, "Mixed invoice totals to 1650");

    // The header is the sum of the rows, by construction.
    var many = new[]
    {
        InvoiceMath.Line(3m, 149.99m, 15m),
        InvoiceMath.Line(1m, 2500.00m, 15m),
        InvoiceMath.Line(7m, 33.33m, 0m),
        InvoiceMath.Line(2m, 19.95m, 15m)
    };
    var totals = InvoiceMath.Totals(many);
    Check(totals.NetTotal == many.Sum(l => l.NetExclVat), "Net total is the sum of the line nets");
    Check(totals.VatTotal == many.Sum(l => l.VatAmount), "VAT total is the sum of the line VAT");
    Check(totals.GrandTotal == totals.NetTotal + totals.VatTotal, "Grand total is net plus VAT");
    Check(totals.GrandTotal == many.Sum(l => l.TotalInclVat), "and equals the sum of the line totals");

    var empty = InvoiceMath.Totals([]);
    Check(empty.NetTotal == 0m && empty.VatTotal == 0m && empty.GrandTotal == 0m,
          "An invoice with no lines totals to nothing, not a crash");

    // A negative rate or quantity is not a rounding question, it is a bug upstream.
    var threw = false;
    try { InvoiceMath.Vat(100m, -1m); } catch (ArgumentOutOfRangeException) { threw = true; }
    Check(threw, "A negative VAT rate is refused rather than quietly applied");
}


// --- Receivables: what a client still owes ----------------------------------------------
// Written before the implementation, like InvoiceMath. This decides whether an invoice reads as
// paid, and whether staff are allowed to credit or receipt against it - the two ways money can be
// made to disappear from a ledger.
{
    // Credits reduce what is owed before receipts do: a credit note cancels part of the charge,
    // it is not a payment.
    Check(ReceivableMath.AmountDue(1150.00m, 0m) == 1150.00m, "No credits means the full invoice is due");
    Check(ReceivableMath.AmountDue(1150.00m, 150.00m) == 1000.00m, "A credit reduces what is due");
    Check(ReceivableMath.AmountDue(1150.00m, 1150.00m) == 0m, "A full credit leaves nothing due");

    Check(ReceivableMath.Outstanding(1150.00m, 0m, 0m) == 1150.00m, "Nothing received, all outstanding");
    Check(ReceivableMath.Outstanding(1150.00m, 0m, 500.00m) == 650.00m, "A part payment reduces the balance");
    Check(ReceivableMath.Outstanding(1150.00m, 150.00m, 1000.00m) == 0m, "Credit plus payment can settle it exactly");

    // Overpayment shows as negative rather than clamping to zero. A client who has paid too much is
    // owed money, and hiding that behind a zero is how it never gets refunded.
    Check(ReceivableMath.Outstanding(1000.00m, 0m, 1200.00m) == -200.00m, "Overpayment shows as negative");

    // Status is derived, never typed in - the same discipline SettlementStatus applies to supplier
    // orders, and for the same reason: a status somebody maintains by hand drifts.
    Check(ReceivableMath.StatusFor(CustomerInvoiceStatus.Issued, 1000m, 0m, 0m) == CustomerInvoiceStatus.Issued,
          "Nothing received stays Issued");
    Check(ReceivableMath.StatusFor(CustomerInvoiceStatus.Issued, 1000m, 0m, 400m) == CustomerInvoiceStatus.PartPaid,
          "A part payment is PartPaid");
    Check(ReceivableMath.StatusFor(CustomerInvoiceStatus.Issued, 1000m, 0m, 1000m) == CustomerInvoiceStatus.Paid,
          "Paid in full is Paid");
    Check(ReceivableMath.StatusFor(CustomerInvoiceStatus.PartPaid, 1000m, 0m, 1200m) == CustomerInvoiceStatus.Paid,
          "Overpayment still reads as Paid, not back to PartPaid");

    // Fully credited with nothing received is settled, not perpetually outstanding. Otherwise a
    // cancelled invoice sits on the receivables list forever.
    Check(ReceivableMath.StatusFor(CustomerInvoiceStatus.Issued, 1000m, 1000m, 0m) == CustomerInvoiceStatus.Paid,
          "A fully credited invoice is settled, not outstanding");

    // A draft or cancelled invoice is not a receivable at all and must not be reclassified by this.
    Check(ReceivableMath.StatusFor(CustomerInvoiceStatus.Draft, 1000m, 0m, 0m) == CustomerInvoiceStatus.Draft,
          "A draft is left alone");
    Check(ReceivableMath.StatusFor(CustomerInvoiceStatus.Cancelled, 1000m, 0m, 500m) == CustomerInvoiceStatus.Cancelled,
          "A cancelled invoice is left alone");

    // Receipts.
    Check(ReceivableMath.CanRecordReceipt(500m, 1000m).Ok, "A receipt within the balance is allowed");
    Check(ReceivableMath.CanRecordReceipt(1000m, 1000m).Ok, "Settling exactly is allowed");
    Check(!ReceivableMath.CanRecordReceipt(0m, 1000m).Ok, "A zero receipt is not a payment");
    Check(!ReceivableMath.CanRecordReceipt(-50m, 1000m).Ok, "A negative receipt is a credit note, not a payment");
    Check(!ReceivableMath.CanRecordReceipt(100m, 0m).Ok, "Nothing outstanding, nothing to receipt");

    // Deliberately allowed: clients do overpay, and refusing to record it means the books do not
    // match the bank. It is flagged on screen rather than refused.
    Check(ReceivableMath.CanRecordReceipt(1200m, 1000m).Ok, "An overpayment can still be recorded");

    // Credit notes. The cap is the whole point: crediting more than was ever charged would
    // manufacture a refund out of nothing.
    Check(ReceivableMath.CanCredit(500m, 1000m, 0m).Ok, "A partial credit is allowed");
    Check(ReceivableMath.CanCredit(1000m, 1000m, 0m).Ok, "Crediting the whole invoice is allowed");
    Check(ReceivableMath.CanCredit(400m, 1000m, 600m).Ok, "Credits can be issued up to the total, cumulatively");
    Check(!ReceivableMath.CanCredit(401m, 1000m, 600m).Ok, "One cent past the total is refused");
    Check(!ReceivableMath.CanCredit(1000m, 1000m, 1000m).Ok, "A fully credited invoice cannot be credited again");
    Check(!ReceivableMath.CanCredit(0m, 1000m, 0m).Ok, "A zero credit note is not a correction");
    Check(!ReceivableMath.CanCredit(-100m, 1000m, 0m).Ok, "A negative credit note is an invoice, not a credit");

    Check(ReceivableMath.CreditHeadroom(1000m, 250m) == 750m, "Headroom is what is left to credit");
    Check(ReceivableMath.CreditHeadroom(1000m, 1000m) == 0m, "No headroom on a fully credited invoice");
}

Console.WriteLine($"All {passed} checks passed.");
