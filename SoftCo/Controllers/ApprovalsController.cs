using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SoftCo.Data;
using SoftCo.Models;
using SoftCo.Services.Approvals;
using SoftCo.Services.Invoicing;
using SoftCo.ViewModels;

namespace SoftCo.Controllers;

/// <summary>
/// The approval workflow: staff submit, the Financial Director decides, staff issue.
///
/// Submitting, withdrawing and issuing are ordinary order work and sit behind
/// <see cref="Roles.CanEditOrders"/>. Deciding sits behind <see cref="Roles.CanApprove"/>, which is
/// a different and much smaller set - and the rule that the approver is not the submitter is
/// enforced in <see cref="ApprovalRules"/> on top of that, because an administrator holds both.
/// </summary>
[Authorize(Roles = Roles.AnyRole)]
public class ApprovalsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IPurchaseOrderApprovalService _approvals;
    private readonly IPaymentReleaseService _releases;
    private readonly ICustomerInvoiceService _invoices;
    private readonly IApprovalNotifier _notifier;
    private readonly UserManager<ApplicationUser> _users;

    public ApprovalsController(AppDbContext db, IPurchaseOrderApprovalService approvals,
                               IPaymentReleaseService releases, ICustomerInvoiceService invoices,
                               IApprovalNotifier notifier, UserManager<ApplicationUser> users)
    {
        _db = db;
        _approvals = approvals;
        _releases = releases;
        _invoices = invoices;
        _notifier = notifier;
        _users = users;
    }

    private Actor Me => new(_users.GetUserId(User), User.Identity?.Name);

    /// <summary>
    /// What is waiting on the Financial Director, oldest first - the queue is a to-do list, and the
    /// thing that has been waiting longest is the thing holding someone up.
    /// </summary>
    /// <summary>
    /// The Financial Director's queue, filtered to exactly one status.
    ///
    /// Previously this was a two-way split between "awaiting decision" and everything else, under
    /// the heading "Decided" - which was read as "Declined" by the first person to use it, because
    /// it was the only other tab and so looked like the pile things go to when they go wrong. A tab
    /// per status means nobody has to interpret a label: approved things are under Approved.
    /// </summary>
    [Authorize(Roles = Roles.CanApprove)]
    public async Task<IActionResult> Queue(ApprovalStatus status = ApprovalStatus.Pending)
    {
        var pendingView = status == ApprovalStatus.Pending;

        var q = _db.Approvals.AsNoTracking().Where(a => a.Status == status);

        var rows = await q
            // Waiting longest first while triaging; most recently decided first when looking back.
            .OrderBy(a => pendingView ? a.RequestedAt : DateTime.MaxValue)
            .ThenByDescending(a => a.DecidedAt)
            .Take(200)
            .Select(a => new ApprovalRowViewModel
            {
                Id = a.Id,
                Kind = a.Kind,
                Status = a.Status,
                // Both kinds lead back to an order, but by different routes: a purchase order IS
                // the subject, while a payment release hangs off a PaymentRequest that belongs to
                // one. Resolved here so the queue stays a single query rather than a union.
                Reference = a.SupplierOrder != null
                    ? (a.SupplierOrder.PoNumber ?? "(not numbered)")
                    : a.PaymentRequest != null
                        ? (a.PaymentRequest.SupplierOrder!.PoNumber ?? "(not numbered)")
                        // An invoice has no number until it is issued, which is the whole point of
                        // the decision being asked for. Saying "not yet issued" rather than "draft"
                        // keeps it from reading as a contradiction beside an Approved badge.
                        : (a.CustomerInvoice!.InvoiceNumber ?? "Invoice - not yet issued"),
                SupplierName = a.SupplierOrder != null
                    ? a.SupplierOrder.Supplier!.Name
                    : a.PaymentRequest != null
                        ? a.PaymentRequest.SupplierOrder!.Supplier!.Name
                        : a.CustomerInvoice!.Client!.Name,
                AmountZar = a.AmountZarAtRequest,
                RequestedAt = a.RequestedAt,
                RequestedByName = a.RequestedByName,
                DecidedAt = a.DecidedAt,
                DecidedByName = a.DecidedByName,
                OrderId = a.SupplierOrderId
                    ?? (a.PaymentRequest != null ? a.PaymentRequest.SupplierOrderId : (int?)null),
                InvoiceId = a.CustomerInvoiceId,
                InvoiceStatus = a.CustomerInvoice != null ? a.CustomerInvoice.Status : null
            })
            .ToListAsync();

        ViewBag.Status = status;
        ViewBag.PendingCount = await _db.Approvals.CountAsync(a => a.Status == ApprovalStatus.Pending);

        return View(rows);
    }

    [Authorize(Roles = Roles.CanApprove)]
    public async Task<IActionResult> Review(int id)
    {
        var vm = await BuildReviewAsync(id);
        return vm is null ? NotFound() : View(vm);
    }

    // --- Decisions ---------------------------------------------------------------------------

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.CanApprove)]
    public async Task<IActionResult> Approve(int id, string? note, CancellationToken ct)
    {
        var kind = await KindOfAsync(id, ct);

        var result = kind switch
        {
            ApprovalKind.PaymentRelease => await _releases.ApproveAsync(id, Me, note, ct),
            ApprovalKind.CustomerInvoice => await _invoices.ApproveAsync(id, Me, note, ct),
            _ => await _approvals.ApproveAsync(id, Me, note, ct)
        };

        await NotifyDecisionAsync(id, result.Ok, ct);

        TempData["Flash"] = result.Ok ? "Approved. The person who raised it has been told." : result.Message;
        return result.Ok ? RedirectToAction(nameof(Queue)) : RedirectToAction(nameof(Review), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.CanApprove)]
    public async Task<IActionResult> Reject(int id, string reason, CancellationToken ct)
    {
        var kind = await KindOfAsync(id, ct);

        var result = kind switch
        {
            ApprovalKind.PaymentRelease => await _releases.RejectAsync(id, Me, reason, ct),
            ApprovalKind.CustomerInvoice => await _invoices.RejectAsync(id, Me, reason, ct),
            _ => await _approvals.RejectAsync(id, Me, reason, ct)
        };

        await NotifyDecisionAsync(id, result.Ok, ct);

        if (!result.Ok)
        {
            TempData["Flash"] = result.Message;
            return RedirectToAction(nameof(Review), new { id });
        }

        TempData["Flash"] = "Rejected. The person who raised it has been told why.";
        return RedirectToAction(nameof(Queue));
    }

    // --- Staff actions on an order -------------------------------------------------------------

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.CanEditOrders)]
    public async Task<IActionResult> Submit(int orderId, string? note, CancellationToken ct)
    {
        var result = await _approvals.SubmitAsync(orderId, Me, note, ct);

        if (!result.Ok)
        {
            TempData["Flash"] = result.Message;
            return RedirectToAction("Details", "Orders", new { id = orderId });
        }

        var order = await LoadOrderAsync(orderId, ct);
        var approval = order?.OpenApproval;

        if (order is not null && approval is not null)
        {
            var link = Url.Action(nameof(Review), "Approvals", new { id = approval.Id }, Request.Scheme);
            await _notifier.NotifyApproversAsync(approval, order, link, ct);
            await _db.SaveChangesAsync(ct);

            TempData["Flash"] = approval.NotificationSent
                ? $"Sent for approval. {approval.NotifiedTo} has been emailed with the purchase order attached."
                : approval.NotificationError ?? "Sent for approval, but the notification email did not go out.";
        }

        return RedirectToAction("Details", "Orders", new { id = orderId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.CanEditOrders)]
    public async Task<IActionResult> Withdraw(int orderId, CancellationToken ct)
    {
        var result = await _approvals.WithdrawAsync(orderId, Me, ct);
        TempData["Flash"] = result.Ok ? "Withdrawn and returned to draft." : result.Message;
        return RedirectToAction("Details", "Orders", new { id = orderId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.CanEditOrders)]
    public async Task<IActionResult> Issue(int orderId, CancellationToken ct)
    {
        var result = await _approvals.IssueAsync(orderId, Me, ct);
        TempData["Flash"] = result.Ok ? "Purchase order issued." : result.Message;
        return RedirectToAction("Details", "Orders", new { id = orderId });
    }

    // --- Helpers --------------------------------------------------------------------------------

    /// <summary>
    /// Emails the submitter once a decision is saved. Done here rather than inside the service so
    /// the service stays free of URL generation, and so a mail failure cannot roll back a decision
    /// that has already been recorded.
    /// </summary>
    private async Task NotifyDecisionAsync(int approvalId, bool decided, CancellationToken ct)
    {
        if (!decided) return;

        var approval = await _db.Approvals.FirstOrDefaultAsync(a => a.Id == approvalId, ct);
        if (approval is null) return;

        if (approval.PaymentRequestId is int requestId)
        {
            var request = await _db.PaymentRequests
                .Include(r => r.PaymentContact)
                .Include(r => r.SupplierOrder).ThenInclude(o => o!.Supplier)
                .AsSplitQuery()
                .FirstOrDefaultAsync(r => r.Id == requestId, ct);

            if (request?.SupplierOrder is null) return;

            var paymentLink = Url.Action("Details", "Orders",
                                         new { id = request.SupplierOrderId }, Request.Scheme);

            await _notifier.NotifyPaymentSubmitterAsync(approval, request, paymentLink, ct);
            await _db.SaveChangesAsync(ct);
            return;
        }

        if (approval.SupplierOrderId is not int orderId) return;

        var order = await LoadOrderAsync(orderId, ct);
        if (order is null) return;

        var link = Url.Action("Details", "Orders", new { id = orderId }, Request.Scheme);
        await _notifier.NotifySubmitterAsync(approval, order, link, ct);
        await _db.SaveChangesAsync(ct);
    }

    private async Task<ApprovalKind?> KindOfAsync(int approvalId, CancellationToken ct) =>
        await _db.Approvals.AsNoTracking()
            .Where(a => a.Id == approvalId)
            .Select(a => (ApprovalKind?)a.Kind)
            .FirstOrDefaultAsync(ct);

    private Task<SupplierOrder?> LoadOrderAsync(int orderId, CancellationToken ct) =>
        _db.SupplierOrders
           .Include(o => o.Supplier)
           .Include(o => o.OrderProjects).ThenInclude(op => op.Project)
           .Include(o => o.Approvals)
           .AsSplitQuery()
           .FirstOrDefaultAsync(o => o.Id == orderId, ct);

    private async Task<ApprovalReviewViewModel?> BuildReviewAsync(int id)
    {
        var approval = await _db.Approvals.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id);

        if (approval is null) return null;

        // A purchase order is its own subject; a payment release points at a PaymentRequest, which
        // in turn belongs to an order. Either way the approver is shown the order.
        PaymentRequest? request = null;

        if (approval.PaymentRequestId is int requestId)
        {
            request = await _db.PaymentRequests.AsNoTracking()
                .Include(r => r.PaymentContact)
                .FirstOrDefaultAsync(r => r.Id == requestId);

            if (request is null) return null;
        }

        // A client invoice has no supplier order behind it at all, so it gets its own screen state
        // rather than being forced through one that assumes an order.
        if (approval.CustomerInvoiceId is int invoiceId)
        {
            var invoice = await _db.CustomerInvoices.AsNoTracking()
                .Include(i => i.Client)
                .Include(i => i.Project)
                .Include(i => i.Lines)
                .AsSplitQuery()
                .FirstOrDefaultAsync(i => i.Id == invoiceId);

            if (invoice is null) return null;

            var invoiceDecision = ApprovalRules.CanDecide(approval.Status, approval.RequestedById,
                                                          _users.GetUserId(User));

            return new ApprovalReviewViewModel
            {
                Approval = approval,
                Invoice = invoice,
                Projects = invoice.Project?.Name ?? "",
                CanDecide = invoiceDecision.Ok,
                WhyNot = invoiceDecision.Message,
                MinimumReasonLength = ApprovalRules.MinimumRejectionReasonLength
            };
        }

        if ((approval.SupplierOrderId ?? request?.SupplierOrderId) is not int orderId) return null;

        var order = await _db.SupplierOrders.AsNoTracking()
            .Include(o => o.Supplier)
            .Include(o => o.OrderProjects).ThenInclude(op => op.Project)
            .AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order is null) return null;

        // Checked here so the screen can explain why the buttons are absent, rather than offering
        // them and refusing on submit. The POST re-checks regardless - this is presentation.
        var canDecide = ApprovalRules.CanDecide(approval.Status, approval.RequestedById, _users.GetUserId(User));

        return new ApprovalReviewViewModel
        {
            Approval = approval,
            Order = order,
            PaymentRequest = request,
            Projects = string.Join(", ", order.OrderProjects
                .Select(op => op.Project?.Name).Where(n => !string.IsNullOrWhiteSpace(n))!),
            CanDecide = canDecide.Ok,
            WhyNot = canDecide.Message,
            MinimumReasonLength = ApprovalRules.MinimumRejectionReasonLength
        };
    }
}
