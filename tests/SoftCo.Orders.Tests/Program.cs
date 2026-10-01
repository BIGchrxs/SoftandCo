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

Console.WriteLine($"All {passed} order checks passed.");
