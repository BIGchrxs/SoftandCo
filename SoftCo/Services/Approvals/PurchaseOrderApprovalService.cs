using Microsoft.EntityFrameworkCore;
using SoftCo.Data;
using SoftCo.Models;

namespace SoftCo.Services.Approvals;

/// <summary>The caller's identity, passed in rather than read from HttpContext so this is testable.</summary>
public sealed record Actor(string? Id, string? Name);

public interface IPurchaseOrderApprovalService
{
    Task<RuleResult> SubmitAsync(int orderId, Actor actor, string? note, CancellationToken ct = default);
    Task<RuleResult> ApproveAsync(int approvalId, Actor actor, string? note, CancellationToken ct = default);
    Task<RuleResult> RejectAsync(int approvalId, Actor actor, string reason, CancellationToken ct = default);
    Task<RuleResult> WithdrawAsync(int orderId, Actor actor, CancellationToken ct = default);
    Task<RuleResult> IssueAsync(int orderId, Actor actor, CancellationToken ct = default);

    /// <summary>
    /// Called after an order is edited. Knocks an approval down if the material facts moved.
    /// Does not save - the caller is mid-edit and owns the transaction.
    /// </summary>
    Task<bool> InvalidateIfChangedAsync(SupplierOrder order, Actor actor, CancellationToken ct = default);

    string Fingerprint(SupplierOrder order);
}

/// <summary>
/// Drives a purchase order through Draft, PendingApproval, Approved and Issued.
///
/// All the judgement lives in <see cref="ApprovalRules"/>; this does the reading, writing and
/// auditing around it. Keeping them apart means the rules can be exercised exhaustively without a
/// database, and this class stays short enough to check by eye.
/// </summary>
public sealed class PurchaseOrderApprovalService : IPurchaseOrderApprovalService
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;

    public PurchaseOrderApprovalService(AppDbContext db, IAuditService audit, TimeProvider clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public string Fingerprint(SupplierOrder order) =>
        SubjectFingerprint.ForPurchaseOrder(
            order.SupplierId, order.CurrencyCode, order.ExchangeRate,
            order.InvoiceValueForeign, order.InvoiceValueZar,
            order.OrderProjects.Select(op => op.ProjectId));

    public async Task<RuleResult> SubmitAsync(int orderId, Actor actor, string? note,
                                              CancellationToken ct = default)
    {
        var order = await LoadAsync(orderId, ct);
        if (order is null) return RuleResult.Refuse("That order no longer exists.");

        var check = ApprovalRules.CanSubmit(
            order.PoApprovalStatus,
            hasSupplier: order.SupplierId > 0,
            valueZar: order.InvoiceValueZar,
            hasProject: order.OrderProjects.Count > 0);

        if (!check.Ok) return check;

        // Resubmitting after a rejection opens a new request. The rejected one stays exactly as it
        // was - it is the record of a decision somebody made, not a draft.
        var approval = new Approval
        {
            Kind = ApprovalKind.PurchaseOrder,
            Status = ApprovalStatus.Pending,
            SupplierOrderId = order.Id,
            AmountZarAtRequest = order.InvoiceValueZar,
            SubjectFingerprint = Fingerprint(order),
            RequestedAt = _clock.GetUtcNow().UtcDateTime,
            RequestedById = actor.Id,
            RequestedByName = actor.Name,
            RequestNote = Trim(note)
        };

        _db.Approvals.Add(approval);
        order.PoApprovalStatus = PoApprovalStatus.PendingApproval;

        _audit.Record(nameof(SupplierOrder), order.Id.ToString(), "PurchaseOrderSubmitted",
                      field: nameof(PoApprovalStatus),
                      newValue: $"R {approval.AmountZarAtRequest:N2}");

        await _db.SaveChangesAsync(ct);
        return RuleResult.Allowed;
    }

    public async Task<RuleResult> ApproveAsync(int approvalId, Actor actor, string? note,
                                               CancellationToken ct = default)
    {
        var approval = await _db.Approvals.FirstOrDefaultAsync(a => a.Id == approvalId, ct);
        if (approval?.SupplierOrderId is not int orderId)
            return RuleResult.Refuse("That approval request no longer exists.");

        var check = ApprovalRules.CanDecide(approval.Status, approval.RequestedById, actor.Id);
        if (!check.Ok) return check;

        var order = await LoadAsync(orderId, ct);
        if (order is null) return RuleResult.Refuse("That order no longer exists.");

        Decide(approval, ApprovalStatus.Approved, actor, note);
        order.PoApprovalStatus = PoApprovalStatus.Approved;

        _audit.Record(nameof(SupplierOrder), orderId.ToString(), "PurchaseOrderApproved",
                      newValue: $"R {approval.AmountZarAtRequest:N2} by {actor.Name}",
                      reason: Trim(note));

        await _db.SaveChangesAsync(ct);
        return RuleResult.Allowed;
    }

    public async Task<RuleResult> RejectAsync(int approvalId, Actor actor, string reason,
                                              CancellationToken ct = default)
    {
        var approval = await _db.Approvals.FirstOrDefaultAsync(a => a.Id == approvalId, ct);
        if (approval?.SupplierOrderId is not int orderId)
            return RuleResult.Refuse("That approval request no longer exists.");

        var check = ApprovalRules.CanReject(approval.Status, approval.RequestedById, actor.Id, reason);
        if (!check.Ok) return check;

        var order = await LoadAsync(orderId, ct);
        if (order is null) return RuleResult.Refuse("That order no longer exists.");

        Decide(approval, ApprovalStatus.Rejected, actor, reason);
        order.PoApprovalStatus = PoApprovalStatus.Rejected;

        _audit.Record(nameof(SupplierOrder), orderId.ToString(), "PurchaseOrderRejected",
                      newValue: actor.Name, reason: Trim(reason));

        await _db.SaveChangesAsync(ct);
        return RuleResult.Allowed;
    }

    public async Task<RuleResult> WithdrawAsync(int orderId, Actor actor, CancellationToken ct = default)
    {
        var order = await LoadAsync(orderId, ct);
        if (order is null) return RuleResult.Refuse("That order no longer exists.");

        var check = ApprovalRules.CanWithdraw(order.PoApprovalStatus);
        if (!check.Ok) return check;

        if (order.OpenApproval is Approval open)
            Decide(open, ApprovalStatus.Withdrawn, actor, "Withdrawn by the submitter.");

        order.PoApprovalStatus = PoApprovalStatus.Draft;

        _audit.Record(nameof(SupplierOrder), orderId.ToString(), "PurchaseOrderWithdrawn",
                      newValue: actor.Name);

        await _db.SaveChangesAsync(ct);
        return RuleResult.Allowed;
    }

    public async Task<RuleResult> IssueAsync(int orderId, Actor actor, CancellationToken ct = default)
    {
        var order = await LoadAsync(orderId, ct);
        if (order is null) return RuleResult.Refuse("That order no longer exists.");

        // The fingerprint recorded on the approval that put this order into Approved, not the most
        // recent one of any kind - a later withdrawn request must not be what issuing is checked
        // against.
        var approved = order.Approvals
            .Where(a => a.Status == ApprovalStatus.Approved)
            .OrderByDescending(a => a.DecidedAt)
            .FirstOrDefault();

        var check = ApprovalRules.CanIssue(order.PoApprovalStatus, approved?.SubjectFingerprint,
                                           Fingerprint(order));

        if (!check.Ok)
        {
            // A fingerprint mismatch is not just a refusal: the order is demonstrably not what was
            // approved, so it goes back to Draft here rather than sitting in Approved looking ready.
            if (order.PoApprovalStatus == PoApprovalStatus.Approved)
            {
                order.PoApprovalStatus = PoApprovalStatus.Draft;
                _audit.Record(nameof(SupplierOrder), orderId.ToString(), "PurchaseOrderApprovalInvalidated",
                              reason: "The order changed after approval and was returned to draft.");
                await _db.SaveChangesAsync(ct);
            }
            return check;
        }

        order.PoApprovalStatus = PoApprovalStatus.Issued;

        _audit.Record(nameof(SupplierOrder), orderId.ToString(), "PurchaseOrderIssued",
                      newValue: $"{order.PoNumber} by {actor.Name}");

        await _db.SaveChangesAsync(ct);
        return RuleResult.Allowed;
    }

    public async Task<bool> InvalidateIfChangedAsync(SupplierOrder order, Actor actor,
                                                     CancellationToken ct = default)
    {
        if (!ApprovalRules.EditInvalidates(order.PoApprovalStatus)) return false;

        var approvals = await _db.Approvals
            .Where(a => a.SupplierOrderId == order.Id)
            .ToListAsync(ct);

        var reference = approvals
            .Where(a => a.Status is ApprovalStatus.Approved or ApprovalStatus.Pending)
            .OrderByDescending(a => a.RequestedAt)
            .FirstOrDefault();

        if (reference is null) return false;
        if (SubjectFingerprint.Matches(reference.SubjectFingerprint, Fingerprint(order))) return false;

        // Something material moved. A pending request is withdrawn rather than left in the queue
        // describing an order that no longer exists in that form.
        if (reference.Status == ApprovalStatus.Pending)
            Decide(reference, ApprovalStatus.Withdrawn, actor,
                   "The order was edited while this request was waiting, so it was withdrawn.");

        order.PoApprovalStatus = PoApprovalStatus.Draft;

        _audit.Record(nameof(SupplierOrder), order.Id.ToString(), "PurchaseOrderApprovalInvalidated",
                      field: nameof(PoApprovalStatus),
                      reason: "A material change was made after submission, so approval was reset.");

        return true;
    }

    private Task<SupplierOrder?> LoadAsync(int orderId, CancellationToken ct) =>
        _db.SupplierOrders
           .Include(o => o.OrderProjects)
           .Include(o => o.Approvals)
           .AsSplitQuery()
           .FirstOrDefaultAsync(o => o.Id == orderId, ct);

    /// <summary>Shared with payment releases, so the two cannot drift apart on how a decision is recorded.</summary>
    private void Decide(Approval approval, ApprovalStatus status, Actor actor, string? reason) =>
        ApprovalDecision.Record(approval, status, actor, reason, _clock.GetUtcNow().UtcDateTime);

    private static string? Trim(string? value) => ApprovalDecision.Trim(value);
}
