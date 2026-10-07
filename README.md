# Soft & Co. order tracker

ASP.NET Core MVC with PostgreSQL and role-based access. The interface follows the supplied Soft & Co.
reference: cream navigation, brown accents, light surfaces and compact tables.

## The shape of the system

Money moves in two directions, and almost every confusing thing in this codebase is explained by
keeping them apart.

**Out.** A `SupplierOrder` is something Soft & Co buy. Its `InvoiceRef`, `InvoiceDate` and
`HasInvoice` all mean *the supplier's invoice to Soft & Co*. On screen these are labelled "Supplier
invoice ref" and so on, so they cannot be confused with the other direction.

**In.** A `CustomerInvoice` is something Soft & Co bill a client for. It is deliberately never named
`Invoice`: a bare `Invoice` type sitting beside `SupplierOrder.InvoiceRef` would be a permanent
source of wrong-direction bugs.

Gross profit is the two halves subtracted, which is why both have to exist before it means anything.

## Approvals

Nothing commits Soft & Co to money without the Financial Director seeing the figure first.

| What | Raised by | Approved by | Then |
|---|---|---|---|
| Purchase order | Staff | Financial Director | Staff issue it |
| Payment release | Staff | Financial Director | Staff send the request |
| Client invoice | Staff | Financial Director | Staff issue it, allocating a number |

All three share one `Approval` table with three nullable subject keys and a
`num_nonnulls(...) = 1` check constraint, so the database enforces that an approval has exactly one
subject. The queue is one indexed query rather than a three-way union, and the rules are written
once.

Two rules are worth knowing:

- **The approver is never the submitter.** An approval somebody can grant themselves is a formality,
  not a control. Enforced on the POST, not just by hiding buttons.
- **Approval covers a specific version.** A purchase order carries a SHA-256 fingerprint over its
  supplier, currency, rate, values and projects. Edit any of those after approval and it returns to
  draft, because what would be issued is no longer what was approved. A payment release does the
  same by comparing the approved amount against what is outstanding at the moment of release.

Rejections require a reason of real length, and the submitter is emailed on both approve and reject
— without that, staff press a button, nothing visibly happens, and they send the request by hand.

## VAT

VAT is stored **per invoice line**, exclusive, with the rate frozen on the line at save.

- One invoice can carry several rates. Goods exported to T.M Mauritius are zero-rated while local
  delivery on the same job is standard-rated, and a single header rate could not represent that.
- The rate is never read from configuration at render time. South Africa moved from 14% to 15% in
  2018; a live lookup would silently re-price every historical invoice the next time it changed.
- VAT is calculated and rounded **per line, then summed**. Computing it on the subtotal produces a
  header that differs by cents from the rows a client can add up themselves.
- Rounding is `MidpointRounding.AwayFromZero` everywhere. The .NET default is banker's rounding,
  which would shave a cent in the same direction on every line of every invoice.

`Services/Invoicing/InvoiceMath.cs` is the only thing that does this arithmetic, and it was written
against its tests before any of it existed.

`CompanyOptions.IsVatRegistered` decides whether a document calls itself a TAX INVOICE and shows the
VAT number — not whether any line happens to carry VAT, because a fully zero-rated export issued by a
registered vendor is still a tax invoice.

## Documents

Purchase orders and client invoices render as PDFs through `IPdfRenderer`, implemented once with
PDFsharp + MigraDoc (MIT, no native assets). Only `MigraDocPdfRenderer` references the library.

The brand faces — Manrope and Cormorant Garamond, both SIL Open Font Licence — are compiled into the
assembly as embedded resources. PDFsharp 6 has no access to system fonts on .NET Core, so this is
what makes a document render identically on a developer's machine and in the Linux container, with
no font package in the Dockerfile. Rendering was verified inside the container, not just locally.

Every number and date on a document is formatted against a fixed culture, never the ambient one. A
machine on a comma-decimal locale would otherwise produce `R 318 622,26`.

## Cost allocation and gross profit

An order can serve several projects. `OrderProject.AllocationShare` says how much of its cost belongs
to each, and an order's shares must sum to 1.

This column is what makes gross profit possible. Without it the join carried no amount, so an order
serving two projects had its **full** value counted into **each** — the per-project figures summed to
R227,133.01 against an actual R156,133.01. Project balances are now additive, where earlier versions
of this file had to warn that they were not.

Existing links were backfilled as an even split. The remainder goes to the last share, because a
third at six decimal places is `0.333333` and three of those come to `0.999999`.

```
Revenue(project) = Σ line.LineNetExclVat on issued invoices − issued credit notes
Cost(project)    = Σ order cost × that project's allocation share
Gross profit     = Revenue − Cost          Margin = GP / Revenue, or null when revenue is 0
```

Revenue excludes VAT on both sides. VAT is SARS's money passing through; counting it would overstate
margin by roughly 15%. A project with cost and no revenue shows **no** margin rather than 0% — those
are different statements.

The report is gated on `CanSeeMargin` (Admin, Finance, Financial Director), which is deliberately
narrower than `CanSeeValues`: what an order cost and what a client was charged are both visible to
the people doing the work, but the difference between them is not.

### The caveat the report carries

`SupplierOrder.InvoiceValueZar` has no inherent VAT semantics. A local invoice of R11,500 normally
contains R1,500 of input VAT that Soft & Co reclaim, so the true cost is R10,000; an imported order
carries no South African VAT at all. The same column therefore means different things row to row.

Existing orders are labelled `SupplierVatTreatment.Unknown` rather than guessed at — dividing them by
1.15 would rewrite financial records on an assumption, and where a supplier was not registered it
would introduce an error that looks exactly like data. Until each order says which it is, its full
value counts as cost, which **understates** margin. The report counts those rows and says so on
screen. Set the treatment on each order as you touch it.

## Receipts and credit notes

Receipts are rows, not columns, for the reason supplier payments are: clients pay in instalments.
They are `Restrict`-linked to their invoice, because money that actually arrived must not vanish
because somebody deleted the document it arrived against.

An issued invoice is immutable. Correcting one is a **credit note**, which is what the VAT Act
requires and what stops this becoming the spreadsheet it replaced. Total credits can never exceed
what was invoiced; a credit note reverses at the rate the invoice was issued at, not today's.

Credit notes come off a client's balance **before** receipts do: a credit cancels part of the charge,
it is not a payment. A negative balance means the client has overpaid, and is shown as such rather
than hidden behind a zero.

## Accounting integration

There is **no Xero code**, and none is planned in this work.

`Client`, `CustomerInvoice`, `CreditNote` and `InvoiceReceipt` each carry `ExternalId`,
`ExternalReference`, `SyncStatus` and `LastSyncedAt`, with `SyncStatus` indexed. A read-only badge
shows the state on detail screens. Everything reads "Not synced" today; the point is that wiring up
an integration later is a feature, not a migration across four tables of live financial records.

The badge is deliberately absent from list rows. A badge that says the same thing on four hundred
rows is not information.

## Local orders, PO numbers and payment requests

Local purchases share the orders table with international ones, separated by an `OrderType` flag, so
payments, project links, settlement calculations and the audit trail are the same code for both.
Local orders are fixed to ZAR at a rate of 1 on the server whatever the form posts, and have no
cargo-readiness date.

Documents are numbered from PostgreSQL sequences through one generator — `PO-2026-0001`,
`INV-2026-0001`, `CN-2026-0001`, each from its own sequence. A sequence is used rather than
`MAX(...) + 1` because `nextval` is atomic under concurrency and never rolls back, so a number
cannot be issued twice. Numbers can therefore skip; that is expected and correct.

PO numbers are allocated when an order is created, because the number is an internal reference from
minute one. Invoice and credit-note numbers are allocated **on issue**, so an abandoned draft does
not burn a number out of a series a tax authority expects to be unbroken.

Attach the supplier invoice on the order page to unlock **Request payment**. Uploads are limited to
PDF, PNG and JPG up to 10 MB, checked against an extension allow-list *and* the file's leading bytes,
so a renamed executable is rejected. Files are stored under `App_Data/order-documents` outside
`wwwroot` under a server-generated GUID name and served only through an authenticated action.

Requesting payment no longer sends anything directly. It raises a release for the Financial Director;
only once approved can staff send it to a contact from the shared address book at `/PaymentContacts`.
The `PaymentRequest` row is written when the request is raised and records both how far the approval
got and whether the email left — two separate questions that must not be conflated.

In Development the composed email is written to `App_Data/sent-email` as a `.eml` file instead of
being sent, so the whole flow can be exercised without credentials. Supply `Email__Host`,
`Email__User`, `Email__Password` and `Email__FromAddress` through user secrets or the environment to
send for real; no code changes.

## Workbook reference

`International Payment Tracker.xlsx` was read as reference data only. Its Suppliers sheet contains 29
order lines and its Service Provider sheet contains 2 freight lines. The development-only
`TrackerSeed` represents these entries and only seeds an empty orders table. The original workbook is
not changed, and nothing imports into or overwrites an existing database.

The existing seed infers currencies from the Rand/foreign ratio, assumes 2026 for day/month dates and
makes payment and status assumptions. Some workbook rows say Outstanding even though their deposit
and settlement columns total the invoice. Those values need business confirmation before the seed is
treated as production financial records.

Summary cards on filtered pages reflect only the displayed results. These are operational views, not
a live spreadsheet sync.

## Before going live

Three things this codebase cannot do for you:

1. **Fill in the `Company` section of `appsettings.json`** — name, address, registration number and
   VAT number. These print on every purchase order and invoice, and the VAT number is legally
   load-bearing: an invoice issued without it is not a valid tax invoice.
2. **Rotate the ExchangeRate-API key.** The commits carrying the old one are on `origin/localDev`, so
   it is published. Removing it from the file does not un-publish it.
3. **Onboard the Financial Director** from Users → Onboard a user. Until an active FD account exists,
   submissions still queue but nobody is notified.

Known gap: **credit notes do not require FD approval.** Anyone with edit rights can credit a full
invoice without the Financial Director seeing it, which is a wider hole than the one the payment
workflow closes. An approval for a credit note could point at the invoice it credits, using the
subject key that already exists.

## Run locally

Requires the .NET 10 SDK and PostgreSQL. Configure `ConnectionStrings__DefaultConnection`,
`Seed__AdminEmail` and `Seed__AdminPassword` for your local environment, then run:

```powershell
dotnet run --project SoftCo/SoftCo.csproj --launch-profile https
```

Use HTTPS at `https://localhost:7027` because authentication cookies require HTTPS. Start the local
database with `docker compose -p softco up -d softco-db`. SoftCo uses host port **5433**, so another
project's PostgreSQL can continue using 5432. The development fallback connects to `localhost:5433`;
the Docker web service connects to `softco-db:5432` inside its network. The named database volume is
retained when the container is recreated. Migrations run at startup. Never use a production database
for browser write tests.

If you set `ConnectionStrings:DefaultConnection` in .NET user secrets or
`ConnectionStrings__DefaultConnection` in your environment, it overrides the development fallback:
use port 5433 and the credentials belonging to the SoftCo database. Changing `POSTGRES_PASSWORD` in
`.env` does not change a password already stored in a PostgreSQL volume.

### A note on `xmin`

Several entities use PostgreSQL's `xmin` system column as an optimistic concurrency token. EF
scaffolds `AddColumn<uint>("xmin", ...)` for these, which PostgreSQL rejects with
`42701: column name "xmin" conflicts with a system column name`. Those lines are removed from the
migrations by hand, with a comment saying so. Do not restore them.

## Verification

```powershell
dotnet build SoftCo/SoftCo.csproj
dotnet run --project tests/SoftCo.ExchangeRates.Tests
dotnet run --project tests/SoftCo.Orders.Tests
```

`tests/SoftCo.Orders.Tests` is a console suite of **238 checks** over the parts of the system where
being almost right is indistinguishable from being wrong until somebody reconciles a VAT return:

- `InvoiceMath` — per-line rounding, away-from-zero at exact halves, inclusive↔exclusive round trips,
  mixed-rate invoices, and that the header always equals the sum of the rows.
- `ReceivableMath` — credits before receipts, overpayment as a negative, the credit cap.
- `GrossProfitMath` — even splits that sum to exactly 1, allocation, and that zero revenue yields no
  margin rather than 0% or infinity.
- `ApprovalRules` and `SubjectFingerprint` — approver ≠ submitter, reason required on reject, and
  that a changed order's fingerprint no longer matches.
- `DocumentNumberFormat`, the upload validator (including a renamed executable and path-traversal
  filenames), sign-in email rules, SA VAT number format, and the derived money properties on
  `SupplierOrder`.

Source is linked rather than project-referenced: these units are deliberately free of EF and
ASP.NET so they can be exercised on their own.

`tests/order-tracker.smoke.cjs` runs real browser checks through login, search, filtering, empty
results, payment-column expansion, supplier/project navigation and mobile layout. It requires
Playwright with Microsoft Edge and a disposable database populated with the development seed.

Set `TRACKER_TEST_URL` (defaults to `https://localhost:7027`), `TRACKER_TEST_EMAIL` and
`TRACKER_TEST_PASSWORD` through your environment. Set `PLAYWRIGHT_MODULE` to an absolute Playwright
module directory if it is not installed on the Node module path. Then run:

```powershell
node tests/order-tracker.smoke.cjs
```

Opt into create/edit/payment checks with `TRACKER_TEST_WRITES=1` only for a disposable test database.
This adds one test order and payment. Screenshots are written to the gitignored `artifacts/`
directory.
