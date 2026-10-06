using Microsoft.EntityFrameworkCore;
using SoftCo.Data;
using SoftCo.Models;

namespace SoftCo.Services.Approvals;

public interface IPaymentReleaseService
{
    /// <summary>
    /// Records that staff want a supplier invoice paid, and puts it to the Financial Director.
    /// Nothing is emailed to the payment contact here.
    /// </summary>
    Task<(RuleResult Result, PaymentRequest? Request)> RaiseAsync(
        int orderId, int contactId, bool attachInvoice, Actor actor, string? note,
        CancellationToken ct = default);

    Task<RuleResult> ApproveAsync(int approvalId, Actor actor, string? note, CancellationToken ct = default);
    Task<RuleResult> RejectAsync(int approvalId, Actor actor, string reason, CancellationToken ct = default);
    Task<RuleResult> WithdrawAsync(int requestId, Actor actor, CancellationToken ct = default);

    /// <summary>
    /// Checks the request may go, and hands it back for the caller to send. Does not email: the
    /// message needs a request-scoped URL, so composing it belongs with the controller, and this
    /// stays testable without a web context.
    /// </summary>
    Task<(RuleResult Result, PaymentRequest? Request)> PrepareReleaseAsync(
        int requestId, CancellationToken ct = default);

    /// <summary>Records the outcome of the send attempt, successful or not.</summary>
    Task RecordSendAsync(PaymentRequest request, Actor actor, string subject,
                         string? error, CancellationToken ct = default);
}

/// <summary>
/// Raise, approve, release. Money leaving Soft &amp; Co now passes a Financial Director before
/// anybody is asked for it, rather than a single button sending the request straight out.
///
/// The judgement lives in <see cref="ApprovalRules"/>; this does the reading, writing and auditing.
/// </summary>
public sealed class PaymentReleaseService : IPaymentReleaseService
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;

    public PaymentReleaseService(AppDbContext db, IAuditService audit, TimeProvider clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<(RuleResult, PaymentRequest?)> RaiseAsync(
        int orderId, int contactId, bool attachInvoice, Actor actor, string? note,
        CancellationToken ct = default)
    {
        var order = await _db.SupplierOrders
            .Include(o => o.Payments)
            .Include(o => o.Documents)
            .Include(o => o.PaymentRequests)
            .AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);

        if (order is null) return (RuleResult.Refuse("That order no longer exists."), null);

        var contact = await _db.PaymentContacts.FirstOrDefaultAsync(c => c.Id == contactId && c.IsActive, ct);

        var check = ApprovalRules.CanRaiseRelease(
            hasInvoice: order.HasInvoice,
            outstandingZar: order.OutstandingZar,
            hasContact: contact is not null,
            alreadyPending: order.PaymentRequests.Any(r => r.IsAwaitingApproval));

        if (!check.Ok) return (check, null);

        var request = new PaymentRequest
        {
            SupplierOrderId = order.Id,
            PaymentContactId = contact!.Id,
            ReleaseStatus = PaymentReleaseStatus.PendingApproval,
            Status = PaymentRequestStatus.NotSent,
            AmountZarAtRequest = order.OutstandingZar,
            AttachInvoice = attachInvoice,
            RaisedAt = _clock.GetUtcNow().UtcDateTime,
            RaisedById = actor.Id,
            RaisedByName = actor.Name
        };

        _db.PaymentRequests.Add(request);

        // Saved before the approval is written, because the approval's subject is a foreign key to
        // this row and the check constraint insists on exactly one subject.
        await _db.SaveChangesAsync(ct);

        _db.Approvals.Add(new Approval
        {
            Kind = ApprovalKind.PaymentRelease,
            Status = ApprovalStatus.Pending,
            PaymentRequestId = request.Id,
            AmountZarAtRequest = request.AmountZarAtRequest,
            RequestedAt = request.RaisedAt,
            RequestedById = actor.Id,
            RequestedByName = actor.Name,
            RequestNote = ApprovalDecision.Trim(note)
        });

        _audit.Record(nameof(SupplierOrder), order.Id.ToString(), "PaymentReleaseRaised",
                      newValue: $"{contact.Name} <{contact.Email}> R {request.AmountZarAtRequest:N2}");

        await _db.SaveChangesAsync(ct);
        return (RuleResult.Allowed, request);
    }

    public async Task<RuleResult> ApproveAsync(int approvalId, Actor actor, string? note,
                                               CancellationToken ct = default)
    {
        var (approval, request, refusal) = await LoadForDecisionAsync(approvalId, actor, ct);
        if (refusal is not null) return refusal;

        ApprovalDecision.Record(approval!, ApprovalStatus.Approved, actor, note, _clock.GetUtcNow().UtcDateTime);
        request!.ReleaseStatus = PaymentReleaseStatus.Approved;

        _audit.Record(nameof(PaymentRequest), request.Id.ToString(), "PaymentReleaseApproved",
                      newValue: $"R {approval!.AmountZarAtRequest:N2} by {actor.Name}",
                      reason: ApprovalDecision.Trim(note));

        await _db.SaveChangesAsync(ct);
        return RuleResult.Allowed;
    }

    public async Task<RuleResult> RejectAsync(int approvalId, Actor actor, string reason,
                                              CancellationToken ct = default)
    {
        var (approval, request, refusal) = await LoadForDecisionAsync(approvalId, actor, ct, reason);
        if (refusal is not null) return refusal;

        ApprovalDecision.Record(approval!, ApprovalStatus.Rejected, actor, reason, _clock.GetUtcNow().UtcDateTime);
        request!.ReleaseStatus = PaymentReleaseStatus.Rejected;

        _audit.Record(nameof(PaymentRequest), request.Id.ToString(), "PaymentReleaseRejected",
                      newValue: actor.Name, reason: ApprovalDecision.Trim(reason));

        await _db.SaveChangesAsync(ct);
        return RuleResult.Allowed;
    }

    public async Task<RuleResult> WithdrawAsync(int requestId, Actor actor, CancellationToken ct = default)
    {
        var request = await _db.PaymentRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null) return RuleResult.Refuse("That payment request no longer exists.");

        var check = ApprovalRules.CanWithdrawRelease(request.ReleaseStatus);
        if (!check.Ok) return check;

        var approval = await OpenApprovalAsync(requestId, ct);
        if (approval is not null)
            ApprovalDecision.Record(approval, ApprovalStatus.Withdrawn, actor,
                                    "Withdrawn by the submitter.", _clock.GetUtcNow().UtcDateTime);

        request.ReleaseStatus = PaymentReleaseStatus.Withdrawn;

        _audit.Record(nameof(PaymentRequest), request.Id.ToString(), "PaymentReleaseWithdrawn",
                      newValue: actor.Name);

        await _db.SaveChangesAsync(ct);
        return RuleResult.Allowed;
    }

    public async Task<(RuleResult, PaymentRequest?)> PrepareReleaseAsync(int requestId,
                                                                        CancellationToken ct = default)
    {
        var request = await _db.PaymentRequests
            .Include(r => r.PaymentContact)
            .Include(r => r.SupplierOrder).ThenInclude(o => o!.Supplier)
            .Include(r => r.SupplierOrder).ThenInclude(o => o!.Payments)
            .Include(r => r.SupplierOrder).ThenInclude(o => o!.Documents)
            .Include(r => r.SupplierOrder).ThenInclude(o => o!.OrderProjects).ThenInclude(op => op.Project)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);

        if (request?.SupplierOrder is null)
            return (RuleResult.Refuse("That payment request no longer exists."), null);

        var check = ApprovalRules.CanRelease(request.ReleaseStatus, request.AmountZarAtRequest,
                                             request.SupplierOrder.OutstandingZar);

        // The amount moved after approval, so what would go out is not what was agreed. The request
        // is closed rather than left sitting there looking ready - the same decision issuing a
        // purchase order makes when its fingerprint no longer matches.
        if (!check.Ok && request.ReleaseStatus == PaymentReleaseStatus.Approved)
        {
            request.ReleaseStatus = PaymentReleaseStatus.Withdrawn;

            _audit.Record(nameof(PaymentRequest), request.Id.ToString(), "PaymentReleaseInvalidated",
                          reason: "The outstanding balance changed after approval.");

            await _db.SaveChangesAsync(ct);
        }

        return (check, check.Ok ? request : null);
    }

    public async Task RecordSendAsync(PaymentRequest request, Actor actor, string subject,
                                      string? error, CancellationToken ct = default)
    {
        request.ReleaseStatus = PaymentReleaseStatus.Released;
        request.Status = error is null ? PaymentRequestStatus.Sent : PaymentRequestStatus.Failed;
        request.ErrorMessage = ApprovalDecision.Trim(error);
        request.Subject = subject;
        request.SentAt = _clock.GetUtcNow().UtcDateTime;
        request.SentById = actor.Id;
        request.SentByName = actor.Name;

        _audit.Record(nameof(SupplierOrder), request.SupplierOrderId.ToString(), "PaymentRequested",
                      field: request.Status.ToString(),
                      newValue: $"{request.PaymentContact?.Name} R {request.AmountZarAtRequest:N2}");

        await _db.SaveChangesAsync(ct);
    }

    private Task<Approval?> OpenApprovalAsync(int requestId, CancellationToken ct) =>
        _db.Approvals
           .Where(a => a.PaymentRequestId == requestId && a.Status == ApprovalStatus.Pending)
           .OrderByDescending(a => a.RequestedAt)
           .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Loads an approval and its request, applying the rule that the approver is not the submitter -
    /// and, when a reason is supplied, that a rejection actually says something.
    /// </summary>
    private async Task<(Approval?, PaymentRequest?, RuleResult?)> LoadForDecisionAsync(
        int approvalId, Actor actor, CancellationToken ct, string? rejectionReason = null)
    {
        var approval = await _db.Approvals.FirstOrDefaultAsync(a => a.Id == approvalId, ct);

        if (approval?.PaymentRequestId is not int requestId)
            return (null, null, RuleResult.Refuse("That approval request no longer exists."));

        var check = rejectionReason is null
            ? ApprovalRules.CanDecide(approval.Status, approval.RequestedById, actor.Id)
            : ApprovalRules.CanReject(approval.Status, approval.RequestedById, actor.Id, rejectionReason);

        if (!check.Ok) return (null, null, check);

        var request = await _db.PaymentRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct);

        return request is null
            ? (null, null, RuleResult.Refuse("That payment request no longer exists."))
            : (approval, request, null);
    }
}
