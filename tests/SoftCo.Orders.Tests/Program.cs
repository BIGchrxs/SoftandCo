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

Console.WriteLine($"All {passed} checks passed.");
