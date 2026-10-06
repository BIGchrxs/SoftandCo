using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SoftCo.Data;
using SoftCo.Models;
using SoftCo.Services;
using SoftCo.Services.Approvals;
using SoftCo.Services.Invoicing;
using SoftCo.Services.Pdf;
using SoftCo.ViewModels;

namespace SoftCo.Controllers;

/// <summary>
/// Invoices Soft &amp; Co issue to clients - the revenue half of the business, which until now the
/// system did not model at all.
///
/// Everything that writes amounts goes through <see cref="ICustomerInvoiceService"/>, which is the
/// only caller of <see cref="InvoiceMath"/>. Nothing here does arithmetic.
/// </summary>
[Authorize(Roles = Roles.AnyRole)]
public class CustomerInvoicesController : Controller
{
    private readonly AppDbContext _db;
    private readonly ICustomerInvoiceService _invoices;
    private readonly ICustomerInvoiceDocumentService _documents;
    private readonly IApprovalNotifier _notifier;
    private readonly IAuditService _audit;
    private readonly UserManager<ApplicationUser> _users;
    private readonly TimeProvider _clock;

    public CustomerInvoicesController(AppDbContext db, ICustomerInvoiceService invoices,
                                      ICustomerInvoiceDocumentService documents, IApprovalNotifier notifier,
                                      IAuditService audit, UserManager<ApplicationUser> users,
                                      TimeProvider clock)
    {
        _db = db;
        _invoices = invoices;
        _documents = documents;
        _notifier = notifier;
        _audit = audit;
        _users = users;
        _clock = clock;
    }

    private Actor Me => new(_users.GetUserId(User), User.Identity?.Name);
    private DateOnly Today => DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);

    [Authorize(Roles = Roles.CanSeeValues)]
    public async Task<IActionResult> Index(CustomerInvoiceStatus? status, int? clientId, int? projectId,
                                           bool overdueOnly = false, int page = 1, int pageSize = 50)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 200);

        var q = InvoiceQueries.Filter(_db.CustomerInvoices.AsNoTracking(),
                                      status, clientId, projectId, overdueOnly, Today);

        var total = await q.CountAsync();

        var rows = await q
            // Drafts first, then the most recently issued: the list is a worklist, not an archive.
            .OrderBy(i => i.IssueDate == null ? 0 : 1)
            .ThenByDescending(i => i.IssueDate)
            .ThenByDescending(i => i.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(i => new InvoiceRowViewModel
            {
                Id = i.Id,
                InvoiceNumber = i.InvoiceNumber,
                Status = i.Status,
                ClientName = i.Client!.Name,
                ProjectName = i.Project!.Name,
                IssueDate = i.IssueDate,
                DueDate = i.DueDate,
                NetTotal = i.NetTotal,
                GrandTotal = i.GrandTotal
            })
            .ToListAsync();

        return View(new InvoiceListViewModel
        {
            Rows = rows,
            Status = status,
            ClientId = clientId,
            ProjectId = projectId,
            OverdueOnly = overdueOnly,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            Clients = await InvoiceQueries.ClientOptions(_db)
        });
    }

    [Authorize(Roles = Roles.CanSeeValues)]
    public async Task<IActionResult> Details(int id)
    {
        var invoice = await _db.CustomerInvoices.AsNoTracking()
            .Include(i => i.Client)
            .Include(i => i.Project)
            .Include(i => i.Lines)
            .Include(i => i.Approvals)
            .AsSplitQuery()
            .FirstOrDefaultAsync(i => i.Id == id);

        if (invoice is null) return NotFound();

        var editable = ApprovalRules.CanEditInvoice(invoice.Status);

        return View(new InvoiceDetailsViewModel
        {
            Invoice = invoice,
            ClientName = invoice.Client?.Name ?? "",
            ProjectName = invoice.Project?.Name,
            CanEdit = editable.Ok && User.CanEditOrders(),
            WhyNotEditable = editable.Message,
            Today = Today
        });
    }

    [Authorize(Roles = Roles.CanEditOrders)]
    public async Task<IActionResult> Create() => View("Edit", await NewFormAsync());

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.CanEditOrders)]
    public async Task<IActionResult> Create(InvoiceEditViewModel vm, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View("Edit", await RefillAsync(vm));

        var client = await _db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == vm.ClientId, ct);
        if (client is null)
        {
            ModelState.AddModelError(nameof(vm.ClientId), "That client is not in the register.");
            return View("Edit", await RefillAsync(vm));
        }

        var invoice = new CustomerInvoice
        {
            ClientId = vm.ClientId,
            ProjectId = vm.ProjectId,
            // Pre-filled from the client, then stored: changing the client's terms later must never
            // move a date already agreed on an invoice.
            PaymentTermsDays = vm.PaymentTermsDays,
            ClientReference = Blank(vm.ClientReference),
            Notes = Blank(vm.Notes),
            InternalNotes = Blank(vm.InternalNotes),
            CreatedById = User.Identity?.Name
        };

        _db.CustomerInvoices.Add(invoice);
        await _db.SaveChangesAsync(ct);

        await _invoices.SaveLinesAsync(invoice.Id, Inputs(vm), vm.PricesEnteredInclusive, ct);

        _audit.Record(nameof(CustomerInvoice), invoice.Id.ToString(), "InvoiceCreated",
                      newValue: $"{client.Name} R {invoice.GrandTotal:N2}");
        await _db.SaveChangesAsync(ct);

        TempData["Flash"] = "Draft invoice created.";
        return RedirectToAction(nameof(Details), new { id = invoice.Id });
    }

    [Authorize(Roles = Roles.CanEditOrders)]
    public async Task<IActionResult> Edit(int id)
    {
        var invoice = await _db.CustomerInvoices.AsNoTracking()
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (invoice is null) return NotFound();

        var editable = ApprovalRules.CanEditInvoice(invoice.Status);
        if (!editable.Ok)
        {
            TempData["Flash"] = editable.Message;
            return RedirectToAction(nameof(Details), new { id });
        }

        var vm = new InvoiceEditViewModel
        {
            Id = invoice.Id,
            ClientId = invoice.ClientId,
            ProjectId = invoice.ProjectId,
            PaymentTermsDays = invoice.PaymentTermsDays,
            ClientReference = invoice.ClientReference,
            PricesEnteredInclusive = invoice.PricesEnteredInclusive,
            Notes = invoice.Notes,
            InternalNotes = invoice.InternalNotes,
            Status = invoice.Status,
            Lines = invoice.Lines.OrderBy(l => l.Sort).Select(l => new InvoiceLineViewModel
            {
                Description = l.Description,
                Quantity = l.Quantity,
                // Shown back in whichever basis was used to enter them, so the figures on screen
                // match what the person typed.
                UnitPrice = invoice.PricesEnteredInclusive ? l.LineTotalInclVat / l.Quantity : l.UnitPriceExclVat,
                VatTreatment = l.VatTreatment
            }).ToList()
        };

        return View(await RefillAsync(vm));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.CanEditOrders)]
    public async Task<IActionResult> Edit(InvoiceEditViewModel vm, CancellationToken ct)
    {
        var invoice = await _db.CustomerInvoices.FirstOrDefaultAsync(i => i.Id == vm.Id, ct);
        if (invoice is null) return NotFound();

        var editable = ApprovalRules.CanEditInvoice(invoice.Status);
        if (!editable.Ok)
        {
            TempData["Flash"] = editable.Message;
            return RedirectToAction(nameof(Details), new { id = vm.Id });
        }

        if (!ModelState.IsValid) return View(await RefillAsync(vm));

        invoice.ClientId = vm.ClientId;
        invoice.ProjectId = vm.ProjectId;
        invoice.PaymentTermsDays = vm.PaymentTermsDays;
        invoice.ClientReference = Blank(vm.ClientReference);
        invoice.Notes = Blank(vm.Notes);
        invoice.InternalNotes = Blank(vm.InternalNotes);

        await _db.SaveChangesAsync(ct);
        await _invoices.SaveLinesAsync(invoice.Id, Inputs(vm), vm.PricesEnteredInclusive, ct);

        TempData["Flash"] = "Invoice updated.";
        return RedirectToAction(nameof(Details), new { id = vm.Id });
    }

    // --- Workflow ------------------------------------------------------------------------------

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.CanEditOrders)]
    public async Task<IActionResult> Submit(int id, string? note, CancellationToken ct)
    {
        var result = await _invoices.SubmitAsync(id, Me, note, ct);

        if (!result.Ok)
        {
            TempData["Flash"] = result.Message;
            return RedirectToAction(nameof(Details), new { id });
        }

        var approval = await _db.Approvals
            .Where(a => a.CustomerInvoiceId == id && a.Status == ApprovalStatus.Pending)
            .OrderByDescending(a => a.RequestedAt)
            .FirstOrDefaultAsync(ct);

        if (approval is not null)
        {
            var link = Url.Action("Review", "Approvals", new { id = approval.Id }, Request.Scheme);
            await _notifier.NotifyInvoiceApproversAsync(approval, link, ct);
            await _db.SaveChangesAsync(ct);

            TempData["Flash"] = approval.NotificationSent
                ? $"Sent for approval. {approval.NotifiedTo} has been emailed."
                : approval.NotificationError ?? "Sent for approval, but the notification email did not go out.";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.CanEditOrders)]
    public async Task<IActionResult> Withdraw(int id, CancellationToken ct)
    {
        var result = await _invoices.WithdrawAsync(id, Me, ct);
        TempData["Flash"] = result.Ok ? "Withdrawn and returned to draft." : result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.CanEditOrders)]
    public async Task<IActionResult> Issue(int id, CancellationToken ct)
    {
        var result = await _invoices.IssueAsync(id, Me, ct);
        TempData["Flash"] = result.Ok
            ? "Invoice issued. It now has a number and cannot be changed - correct it with a credit note."
            : result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Roles = Roles.CanSeeValues)]
    public async Task<IActionResult> Pdf(int id, CancellationToken ct)
    {
        var rendered = await _documents.RenderAsync(id, ct);
        if (rendered is null) return NotFound();

        var (fileName, content) = rendered.Value;
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(content, "application/pdf", fileName);
    }

    // --- Helpers -------------------------------------------------------------------------------

    private static List<InvoiceLineInput> Inputs(InvoiceEditViewModel vm) =>
        vm.Lines
          .Where(l => !string.IsNullOrWhiteSpace(l.Description))
          .Select(l => new InvoiceLineInput(l.Description!, l.Quantity, l.UnitPrice, l.VatTreatment))
          .ToList();

    private async Task<InvoiceEditViewModel> NewFormAsync() => await RefillAsync(new InvoiceEditViewModel());

    private async Task<InvoiceEditViewModel> RefillAsync(InvoiceEditViewModel vm)
    {
        vm.Clients = await InvoiceQueries.ClientOptions(_db, vm.ClientId);
        vm.Projects = await InvoiceQueries.ProjectOptions(_db, vm.ClientId == 0 ? null : vm.ClientId);
        vm.StandardRatePercent = _invoices.RateFor(VatTreatment.Standard);

        // The form always shows a spare row, so adding a line never needs a round trip.
        if (vm.Lines.Count == 0 || vm.Lines.All(l => !string.IsNullOrWhiteSpace(l.Description)))
            vm.Lines.Add(new InvoiceLineViewModel());

        return vm;
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
