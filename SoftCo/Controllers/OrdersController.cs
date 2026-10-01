using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SoftCo.Data;
using SoftCo.Models;
using SoftCo.Services;
using SoftCo.Services.Documents;
using SoftCo.Services.Email;
using SoftCo.ViewModels;

namespace SoftCo.Controllers;

/// <summary>
/// The International Payment Tracker. This is the screen that replaces the spreadsheet:
/// what was ordered from whom, for which projects, where it is, and what has been paid.
/// </summary>
[Authorize]
public class OrdersController : Controller
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly IPoNumberGenerator _poNumbers;
    private readonly IDocumentStore _documents;
    private readonly IEmailService _email;

    public OrdersController(AppDbContext db, IAuditService audit, IPoNumberGenerator poNumbers,
                            IDocumentStore documents, IEmailService email)
    {
        _db = db;
        _audit = audit;
        _poNumbers = poNumbers;
        _documents = documents;
        _email = email;
    }

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

        await _db.SaveChangesAsync();

        TempData["Flash"] = "Order updated.";
        return RedirectToAction(nameof(Details), new { id = order.Id });
    }

    //delete is not implemented because the brief says "no deletion of orders".
    //However, for dev testing i need a delete, ill implement it then delete the code after testing is done.

    public async Task<IActionResult> Delete(int id)
    {
        var order = await _db.SupplierOrders
            .Include(o => o.OrderProjects)
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == id);
        if (order is null) return NotFound();
        _db.OrderPayments.RemoveRange(order.Payments);
        _db.OrderProjects.RemoveRange(order.OrderProjects);
        _db.SupplierOrders.Remove(order);
        await _db.SaveChangesAsync();
        TempData["Flash"] = "Order deleted.";
        return RedirectToAction(nameof(Index));
    }

    


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
    /// Emails the chosen contact asking for this invoice to be paid, and records that it
    /// happened. The PaymentRequest row is written either way: a failure that left no trace
    /// would let someone believe a request went out when it never did.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.CanRecordPayments)]
    public async Task<IActionResult> RequestPayment(int id, int contactId, bool attachInvoice = true)
    {
        var order = await _db.SupplierOrders
            .Include(o => o.Supplier)
            .Include(o => o.OrderProjects).ThenInclude(op => op.Project)
            .Include(o => o.Payments)
            .Include(o => o.Documents)
            .AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order is null) return NotFound();

        var contact = await _db.PaymentContacts.FirstOrDefaultAsync(c => c.Id == contactId && c.IsActive);
        if (contact is null)
        {
            TempData["Flash"] = "Choose who the request should go to.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // Gate: no invoice on the record, nothing to ask anyone to pay.
        if (!order.HasInvoice)
        {
            TempData["Flash"] = "Attach the supplier invoice before requesting payment.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var subject = $"Payment request - {order.PoNumber} - {order.Supplier?.Name}";
        var message = new EmailMessage(contact.Email, contact.Name, subject,
                                       BuildBody(order, contact), BuildAttachments(order, attachInvoice));

        var request = new PaymentRequest
        {
            SupplierOrderId = order.Id,
            PaymentContactId = contact.Id,
            Subject = subject,
            AmountZarAtRequest = order.OutstandingZar,
            SentById = User.Identity?.Name,
            SentByName = User.Identity?.Name
        };

        try
        {
            await _email.SendAsync(message);
            request.Status = PaymentRequestStatus.Sent;
            TempData["Flash"] = $"Payment request {_email.DeliveryDescription} for {contact.Name}.";
        }
        catch (EmailException ex)
        {
            request.Status = PaymentRequestStatus.Failed;
            request.ErrorMessage = ex.Message;
            TempData["Flash"] = ex.Message;
        }

        _db.PaymentRequests.Add(request);

        _audit.Record(nameof(SupplierOrder), order.Id.ToString(), "PaymentRequested",
                      field: request.Status.ToString(),
                      newValue: $"{contact.Name} <{contact.Email}> R {order.OutstandingZar:N2}");

        await _db.SaveChangesAsync();

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
