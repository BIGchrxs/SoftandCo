using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SoftCo.Data;
using SoftCo.Models;
using SoftCo.Services;
using SoftCo.Services.Documents;
using SoftCo.Services.Approvals;
using SoftCo.Services.Email;
using SoftCo.ViewModels;

namespace SoftCo.Controllers;

/// <summary>
/// The International Payment Tracker. This is the screen that replaces the spreadsheet:
/// what was ordered from whom, for which projects, where it is, and what has been paid.
/// </summary>
[Authorize(Roles = Roles.AnyRole)]
public class OrdersController : Controller
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly IPoNumberGenerator _poNumbers;
    private readonly IDocumentStore _documents;
    private readonly IEmailService _email;
    private readonly IPurchaseOrderApprovalService _approvals;
    private readonly IPaymentReleaseService _releases;
    private readonly IApprovalNotifier _notifier;
    private readonly UserManager<ApplicationUser> _users;

    public OrdersController(AppDbContext db, IAuditService audit, IPoNumberGenerator poNumbers,
                            IDocumentStore documents, IEmailService email,
                            IPurchaseOrderApprovalService approvals, IPaymentReleaseService releases,
                            IApprovalNotifier notifier, UserManager<ApplicationUser> users)
    {
        _db = db;
        _audit = audit;
        _poNumbers = poNumbers;
        _documents = documents;
        _email = email;
        _approvals = approvals;
        _releases = releases;
        _notifier = notifier;
        _users = users;
    }

    private Actor Me => new(_users.GetUserId(User), User.Identity?.Name);

    // --- Grid ----------------------------------------------------------------------------

    public async Task<IActionResult> Index(OrderFilterViewModel filter)
    {
        // International only - local purchases have their own page.
        var q = OrderQueries.ApplyFilters(
            OrderQueries.ForGrid(_db, OrderType.International), filter);

        var orders = await q
            .OrderByDescending(o => o.InvoiceDate ?? DateOnly.MinValue)
            .ThenBy(o => o.Supplier!.Name)
            .ToListAsync();

        var rows = orders.Select(OrderQueries.ToRow).ToList();

        // Settlement status is computed from payments, so it cannot be filtered in SQL.
        if (filter.Settlement is SettlementStatus ss)
            rows = rows.Where(r => r.Settlement == ss).ToList();

        var vm = new OrderListViewModel
        {
            Filter = filter,
            Rows = rows,
            Suppliers = await OrderQueries.SupplierOptions(_db),
            Projects = await OrderQueries.ProjectOptions(_db)
        };

        return View(vm);
    }

    /// <summary>Everything not fully settled, grouped by supplier — the morning view.</summary>
    public async Task<IActionResult> Outstanding()
    {
        var orders = await _db.SupplierOrders
            .AsNoTracking()
            .Include(o => o.Supplier)
            .Include(o => o.OrderProjects).ThenInclude(op => op.Project)
            .Include(o => o.Payments)
            .AsSplitQuery()
            .ToListAsync();

        var rows = orders
            .Select(OrderQueries.ToRow)
            .Where(r => r.Settlement != SettlementStatus.Paid)
            .OrderByDescending(r => r.OutstandingZar)
            .ToList();

        var vm = new OrderListViewModel { Rows = rows };
        return View(vm);
    }

    // --- Detail --------------------------------------------------------------------------

    public async Task<IActionResult> Details(int id)
    {
        var order = await _db.SupplierOrders
            .Include(o => o.Supplier)
            .Include(o => o.OrderProjects).ThenInclude(op => op.Project)
            .Include(o => o.Payments)
            .Include(o => o.Documents)
            .Include(o => o.PaymentRequests).ThenInclude(r => r.PaymentContact)
            .Include(o => o.Approvals)
            .AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order is null) return NotFound();

        // The request-payment panel only has anything to offer once an invoice is attached.
        ViewBag.RequestPayment = new RequestPaymentViewModel
        {
            SupplierOrderId = order.Id,
            PoNumber = order.PoNumber ?? "",
            Supplier = order.Supplier?.Name ?? "",
            Product = order.ProductDescription,
            OutstandingZar = order.OutstandingZar,
            Contacts = await _db.PaymentContacts.AsNoTracking()
                .Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync(),
            History = order.PaymentRequests.OrderByDescending(r => r.SentAt).ToList(),
            DeliveryDescription = _email.DeliveryDescription
        };

        ViewBag.History = await _db.AuditEvents.AsNoTracking()
            .Where(a => a.EntityName == nameof(SupplierOrder) && a.EntityId == id.ToString())
            .OrderByDescending(a => a.OccurredAt)
            .Take(25)
            .ToListAsync();

        return View(order);
    }

    // --- Create / Edit -------------------------------------------------------------------

    [Authorize(Roles = Roles.CanEditOrders)]
    public async Task<IActionResult> Create()
    {
        var vm = new OrderEditViewModel
        {
            AllSuppliers = await OrderQueries.SupplierOptions(_db),
            AllProjects = await OrderQueries.ProjectOptions(_db)
        };
        return View("Edit", vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.CanEditOrders)]
    public async Task<IActionResult> Create(OrderEditViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            vm.AllSuppliers = await OrderQueries.SupplierOptions(_db);
            vm.AllProjects = await OrderQueries.ProjectOptions(_db);
            return View("Edit", vm);
        }

        var order = new SupplierOrder
        {
            // Allocated once, here, and never edited afterwards.
            PoNumber = await _poNumbers.NextAsync(),
            OrderType = OrderType.International,
            SupplierId = vm.SupplierId,
            ProductDescription = vm.ProductDescription,
            FulfilmentStatus = vm.FulfilmentStatus,
            CargoReadinessDate = vm.CargoReadinessDate,
            InvoiceRef = vm.InvoiceRef,
            InvoiceDate = vm.InvoiceDate,
            CurrencyCode = vm.CurrencyCode.ToUpperInvariant(),
            ExchangeRate = vm.ExchangeRate,
            InvoiceValueForeign = vm.InvoiceValueForeign,
            InvoiceValueZar = vm.InvoiceValueZar,
            Notes = vm.Notes,
            CreatedAt = DateTime.UtcNow,
            CreatedById = User.Identity?.Name
        };

        foreach (var pid in vm.ProjectIds.Distinct())
            order.OrderProjects.Add(new OrderProject { ProjectId = pid });

        _db.SupplierOrders.Add(order);
        await _db.SaveChangesAsync();

        _audit.Record(nameof(SupplierOrder), order.Id.ToString(), "Created",
                      newValue: $"{order.ProductDescription} / {order.InvoiceRef}");
        await _db.SaveChangesAsync();

        TempData["Flash"] = "Order created.";
        return RedirectToAction(nameof(Details), new { id = order.Id });
    }

    [Authorize(Roles = Roles.CanEditOrders)]
    public async Task<IActionResult> Edit(int id)
    {
        var order = await _db.SupplierOrders
            .Include(o => o.OrderProjects)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order is null) return NotFound();

        var vm = new OrderEditViewModel
        {
            Id = order.Id,
            PoNumber = order.PoNumber,
            OrderType = order.OrderType,
            SupplierId = order.SupplierId,
            ProductDescription = order.ProductDescription,
            ProjectIds = order.OrderProjects.Select(op => op.ProjectId).ToList(),
            FulfilmentStatus = order.FulfilmentStatus,
            CargoReadinessDate = order.CargoReadinessDate,
            InvoiceRef = order.InvoiceRef,
            InvoiceDate = order.InvoiceDate,
            CurrencyCode = order.CurrencyCode,
            ExchangeRate = order.ExchangeRate,
            InvoiceValueForeign = order.InvoiceValueForeign,
            InvoiceValueZar = order.InvoiceValueZar,
            Notes = order.Notes,
            AllSuppliers = await OrderQueries.SupplierOptions(_db),
            AllProjects = await OrderQueries.ProjectOptions(_db)
        };

        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.CanEditOrders)]
    public async Task<IActionResult> Edit(OrderEditViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            vm.AllSuppliers = await OrderQueries.SupplierOptions(_db);
            vm.AllProjects = await OrderQueries.ProjectOptions(_db);
            return View(vm);
        }

        var order = await _db.SupplierOrders
            .Include(o => o.OrderProjects)
            .FirstOrDefaultAsync(o => o.Id == vm.Id);

        if (order is null) return NotFound();

        // Field-level audit: the brief requires previous and new value, not just "changed".
        TrackChange(order.Id, "FulfilmentStatus", order.FulfilmentStatus.ToString(), vm.FulfilmentStatus.ToString());
        TrackChange(order.Id, "InvoiceValueForeign", order.InvoiceValueForeign.ToString(), vm.InvoiceValueForeign.ToString());
        TrackChange(order.Id, "InvoiceValueZar", order.InvoiceValueZar.ToString(), vm.InvoiceValueZar.ToString());
        TrackChange(order.Id, "ExchangeRate", order.ExchangeRate.ToString(), vm.ExchangeRate.ToString());
        TrackChange(order.Id, "InvoiceRef", order.InvoiceRef, vm.InvoiceRef);

        order.SupplierId = vm.SupplierId;
        order.ProductDescription = vm.ProductDescription;
        order.FulfilmentStatus = vm.FulfilmentStatus;
        order.CargoReadinessDate = vm.CargoReadinessDate;
        order.InvoiceRef = vm.InvoiceRef;
        order.InvoiceDate = vm.InvoiceDate;
        order.CurrencyCode = vm.CurrencyCode.ToUpperInvariant();
        order.ExchangeRate = vm.ExchangeRate;
        order.InvoiceValueForeign = vm.InvoiceValueForeign;
        order.InvoiceValueZar = vm.InvoiceValueZar;
        order.Notes = vm.Notes;
        order.UpdatedAt = DateTime.UtcNow;
        order.UpdatedById = User.Identity?.Name;

        var wanted = vm.ProjectIds.Distinct().ToHashSet();
        foreach (var gone in order.OrderProjects.Where(op => !wanted.Contains(op.ProjectId)).ToList())
            order.OrderProjects.Remove(gone);
        foreach (var pid in wanted.Where(p => order.OrderProjects.All(op => op.ProjectId != p)))
            order.OrderProjects.Add(new OrderProject { ProjectId = pid });

        // Approval is of a specific supplier, amount, rate and set of projects. If any of those
        // just moved, the decision no longer describes this order, so it goes back to draft rather
        // than being issued on terms nobody signed off. Runs inside this SaveChanges deliberately:
        // the edit and the invalidation are one change or neither.
        var invalidated = await _approvals.InvalidateIfChangedAsync(
            order, new Actor(_users.GetUserId(User), User.Identity?.Name));

        await _db.SaveChangesAsync();

        TempData["Flash"] = invalidated
            ? "Order updated. It had already been approved, so it has been returned to draft and needs approving again."
            : "Order updated.";
        return RedirectToAction(nameof(Details), new { id = order.Id });
    }

    // The dev-only Delete action was removed. It was a GET with no antiforgery token and no role
    // of its own, so it inherited the class-level AnyRole - which includes Viewer - meaning any
    // signed-in user could destroy an order and its payment history by visiting a URL, and a
    // link-prefetching browser could do it unprompted. The brief says orders are never deleted;
    // cancelling an order (keeping it visible and auditable) is the supported path.

    


    // --- Payments ------------------------------------------------------------------------

    [Authorize(Roles = Roles.CanRecordPayments)]
    public async Task<IActionResult> RecordPayment(int id)
    {
        var order = await _db.SupplierOrders
            .Include(o => o.Supplier)
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order is null) return NotFound();

        var vm = new RecordPaymentViewModel
        {
            SupplierOrderId = order.Id,
            OrderSummary = $"{order.Supplier?.Name} — {order.ProductDescription}",
            CurrencyCode = order.CurrencyCode,
            ExchangeRate = order.ExchangeRate,
            OutstandingForeign = order.OutstandingForeign,
            Kind = order.Payments.Any(p => p.Kind == PaymentKind.Deposit)
                   ? PaymentKind.Settlement : PaymentKind.Deposit,
            AmountForeign = order.OutstandingForeign > 0 ? order.OutstandingForeign : 0
        };

        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.CanRecordPayments)]
    public async Task<IActionResult> RecordPayment(RecordPaymentViewModel vm)
    {
        var order = await _db.SupplierOrders
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == vm.SupplierOrderId);

        if (order is null) return NotFound();

        if (!ModelState.IsValid)
        {
            vm.OutstandingForeign = order.OutstandingForeign;
            return View(vm);
        }

        _db.OrderPayments.Add(new OrderPayment
        {
            SupplierOrderId = order.Id,
            Kind = vm.Kind,
            AmountForeign = vm.AmountForeign,
            AmountZar = vm.AmountZar > 0 ? vm.AmountZar : Math.Round(vm.AmountForeign * order.ExchangeRate, 2),
            PaidDate = vm.PaidDate,
            Reference = vm.Reference,
            CreatedById = User.Identity?.Name
        });

        _audit.Record(nameof(SupplierOrder), order.Id.ToString(), "PaymentRecorded",
                      field: vm.Kind.ToString(),
                      newValue: $"{vm.AmountForeign} {order.CurrencyCode}");

        await _db.SaveChangesAsync();

        TempData["Flash"] = $"{vm.Kind} recorded.";
        return RedirectToAction(nameof(Details), new { id = order.Id });
    }

    // --- Helpers -------------------------------------------------------------------------

    private void TrackChange(int orderId, string field, string? oldV, string? newV)
    {
        if (string.Equals(oldV, newV, StringComparison.Ordinal)) return;
        _audit.Record(nameof(SupplierOrder), orderId.ToString(), "Updated", field, oldV, newV);
    }

    // --- Documents -----------------------------------------------------------------------

    /// <summary>
    /// Attaches a file to an order. RequestSizeLimit rejects an oversized body before it is
    /// buffered; UploadValidator then re-checks the length, the extension against an allow-list
    /// and the leading bytes against the declared type.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.CanEditOrders)]
    [RequestSizeLimit(UploadValidator.MaxBytes + 4096)]
    public async Task<IActionResult> UploadDocument(int id, IFormFile? file, DocumentKind kind)
    {
        var order = await _db.SupplierOrders.FirstOrDefaultAsync(o => o.Id == id);
        if (order is null) return NotFound();

        if (file is null)
        {
            TempData["Flash"] = "Choose a file to attach.";
            return RedirectToAction(nameof(Details), new { id });
        }

        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream);

        var check = UploadValidator.Check(file.FileName, stream.Length, stream);
        if (!check.Ok)
        {
            TempData["Flash"] = check.Error;
            return RedirectToAction(nameof(Details), new { id });
        }

        var doc = await _documents.SaveAsync(order.Id, stream, file.FileName, check, kind,
                                             User.Identity?.Name);
        _db.OrderDocuments.Add(doc);

        _audit.Record(nameof(SupplierOrder), order.Id.ToString(), "DocumentUploaded",
                      field: kind.ToString(), newValue: doc.OriginalFileName);

        await _db.SaveChangesAsync();

        TempData["Flash"] = $"{doc.OriginalFileName} attached.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // --- Payment requests ----------------------------------------------------------------

    /// <summary>
    /// Adds a contact to the shared address book without leaving the order, then returns to it.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.CanEditOrders)]
    public async Task<IActionResult> AddPaymentContact(int id, string name, string email, string? roleNote)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email))
        {
            TempData["Flash"] = "A contact needs both a name and an email address.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var normalised = email.Trim().ToLowerInvariant();

        if (await _db.PaymentContacts.AnyAsync(c => c.Email == normalised))
        {
            TempData["Flash"] = "That email address is already in the contact list.";
            return RedirectToAction(nameof(Details), new { id });
        }

        _db.PaymentContacts.Add(new PaymentContact
        {
            Name = name.Trim(),
            Email = normalised,
            RoleNote = roleNote?.Trim(),
            CreatedById = User.Identity?.Name
        });

        await _db.SaveChangesAsync();

        TempData["Flash"] = $"{name.Trim()} added to payment contacts.";
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>
    /// Raises a payment release and puts it to the Financial Director. Nothing is emailed to the
    /// payment contact here - that was the old behaviour, where one click sent the request straight
    /// out, and it is exactly what this phase removes.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.CanRecordPayments)]
    public async Task<IActionResult> RequestPayment(int id, int contactId, bool attachInvoice = true,
                                                    string? note = null, CancellationToken ct = default)
    {
        var (result, request) = await _releases.RaiseAsync(id, contactId, attachInvoice, Me, note, ct);

        if (!result.Ok || request is null)
        {
            TempData["Flash"] = result.Message;
            return RedirectToAction(nameof(Details), new { id });
        }

        var approval = await _db.Approvals
            .Where(a => a.PaymentRequestId == request.Id && a.Status == ApprovalStatus.Pending)
            .OrderByDescending(a => a.RequestedAt)
            .FirstOrDefaultAsync(ct);

        if (approval is not null)
        {
            var link = Url.Action("Review", "Approvals", new { id = approval.Id }, Request.Scheme);
            await _notifier.NotifyPaymentApproversAsync(approval, request, link, ct);
            await _db.SaveChangesAsync(ct);

            TempData["Flash"] = approval.NotificationSent
                ? $"Sent to {approval.NotifiedTo} for approval. Nothing goes to the supplier contact until it is approved."
                : approval.NotificationError ?? "Raised for approval, but the notification email did not go out.";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>
    /// Sends the approved payment request to the chosen contact. This is the only place an email
    /// about paying a supplier actually leaves, and it cannot be reached without approval.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.CanRecordPayments)]
    public async Task<IActionResult> ReleasePayment(int id, int requestId, CancellationToken ct = default)
    {
        var (check, request) = await _releases.PrepareReleaseAsync(requestId, ct);

        if (!check.Ok || request?.SupplierOrder is null || request.PaymentContact is null)
        {
            TempData["Flash"] = check.Message;
            return RedirectToAction(nameof(Details), new { id });
        }

        var order = request.SupplierOrder;
        var contact = request.PaymentContact;
        var subject = $"Payment request - {order.PoNumber} - {order.Supplier?.Name}";

        var message = new EmailMessage(contact.Email, contact.Name, subject,
                                       BuildBody(order, contact),
                                       BuildAttachments(order, request.AttachInvoice));

        string? error = null;
        try
        {
            await _email.SendAsync(message, ct);
            TempData["Flash"] = $"Payment request {_email.DeliveryDescription} for {contact.Name}.";
        }
        catch (EmailException ex)
        {
            // The row is written either way. A failure that left no trace would let someone believe
            // a request went out when it never did.
            error = ex.Message;
            TempData["Flash"] = ex.Message;
        }

        await _releases.RecordSendAsync(request, Me, subject, error, ct);

        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>Pulls a raised request back before anyone has decided it.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.CanRecordPayments)]
    public async Task<IActionResult> WithdrawPaymentRequest(int id, int requestId, CancellationToken ct = default)
    {
        var result = await _releases.WithdrawAsync(requestId, Me, ct);
        TempData["Flash"] = result.Ok ? "Payment request withdrawn." : result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    private string BuildBody(SupplierOrder order, PaymentContact contact)
    {
        var projects = string.Join(", ", order.OrderProjects.Select(op => op.Project?.Name).Where(n => n != null));
        var link = Url.Action(nameof(Details), "Orders", new { id = order.Id }, Request.Scheme);

        return $"""
            Hi {contact.Name},

            Please arrange payment for the following supplier invoice.

            PO number      {order.PoNumber}
            Supplier       {order.Supplier?.Name}
            Product        {order.ProductDescription}
            Project(s)     {(string.IsNullOrWhiteSpace(projects) ? "-" : projects)}
            Invoice ref    {order.InvoiceRef ?? "-"}
            Invoice date   {(order.InvoiceDate?.ToString("dd MMM yyyy") ?? "-")}

            Invoice value  R {order.InvoiceValueZar:N2} ({order.InvoiceValueForeign:N2} {order.CurrencyCode})
            Already paid   R {order.PaidZar:N2}
            Outstanding    R {order.OutstandingZar:N2}

            Full record: {link}

            Sent from the Soft & Co. order tracker by {User.Identity?.Name}.
            """;
    }

    /// <summary>
    /// Attaches the most recent invoice when it is small enough to mail. Larger files stay
    /// behind the authenticated download link in the body rather than bloating the message.
    /// </summary>
    private List<EmailAttachment> BuildAttachments(SupplierOrder order, bool attachInvoice)
    {
        const long maxAttachment = 5 * 1024 * 1024;
        var list = new List<EmailAttachment>();

        if (!attachInvoice) return list;

        var invoice = order.Documents
            .Where(d => d.Kind == DocumentKind.Invoice)
            .OrderByDescending(d => d.UploadedAt)
            .FirstOrDefault();

        if (invoice is null || invoice.SizeBytes > maxAttachment || !_documents.Exists(invoice))
            return list;

        using var stream = _documents.OpenRead(invoice);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);

        list.Add(new EmailAttachment(invoice.OriginalFileName, invoice.ContentType, buffer.ToArray()));
        return list;
    }

}
