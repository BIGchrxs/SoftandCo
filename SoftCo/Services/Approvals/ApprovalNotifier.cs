using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SoftCo.Data;
using SoftCo.Models;
using SoftCo.Services.Email;
using SoftCo.Services.Pdf;

namespace SoftCo.Services.Approvals;

public interface IApprovalNotifier
{
    /// <summary>
    /// Tells the Financial Director there is something waiting, with the purchase order attached.
    /// Records the outcome on the approval either way and never throws - a notification that fails
    /// must not undo a submission that succeeded.
    /// </summary>
    Task NotifyApproversAsync(Approval approval, SupplierOrder order, string? link,
                              CancellationToken ct = default);

    /// <summary>Tells the person who submitted it what was decided.</summary>
    Task NotifySubmitterAsync(Approval approval, SupplierOrder order, string? link,
                              CancellationToken ct = default);
}

/// <summary>
/// The email around an approval.
///
/// Failures are recorded rather than thrown, for the reason <c>PaymentRequest</c> keeps its own:
/// "I sent it for approval" must never stand for a message that did not leave. The difference shows
/// on screen, so somebody can pick up the phone.
/// </summary>
public sealed class ApprovalNotifier : IApprovalNotifier
{
    private readonly IEmailService _email;
    private readonly IPurchaseOrderDocumentService _documents;
    private readonly UserManager<ApplicationUser> _users;
    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<ApprovalNotifier> _log;

    /// <summary>Mail servers reject large attachments; the link in the body always works.</summary>
    private const long MaxAttachmentBytes = 5 * 1024 * 1024;

    public ApprovalNotifier(IEmailService email, IPurchaseOrderDocumentService documents,
                            UserManager<ApplicationUser> users, AppDbContext db,
                            TimeProvider clock, ILogger<ApprovalNotifier> log)
    {
        _email = email;
        _documents = documents;
        _users = users;
        _db = db;
        _clock = clock;
        _log = log;
    }

    public async Task NotifyApproversAsync(Approval approval, SupplierOrder order, string? link,
                                           CancellationToken ct = default)
    {
        var approvers = await ActiveApproversAsync(ct);

        if (approvers.Count == 0)
        {
            // Not an error that should lose the submission, but it does mean nobody has been told.
            // Recorded so the screen can say so rather than implying the request is on its way.
            approval.NotificationSent = false;
            approval.NotificationError =
                "No active Financial Director account to notify. The request is in the approval queue.";
            return;
        }

        var subject = $"Approval needed - {order.PoNumber} - R {approval.AmountZarAtRequest:N2}";
        var attachments = await AttachmentsAsync(order.Id, ct);

        foreach (var approver in approvers)
        {
            var body = ApproverBody(approval, order, approver, link, attachments.Count > 0);
            await SendAsync(approval, new EmailMessage(approver.Email!, DisplayName(approver),
                                                       subject, body, attachments), ct);
        }

        approval.NotifiedTo = string.Join(", ", approvers.Select(a => a.Email));
    }

    public async Task NotifySubmitterAsync(Approval approval, SupplierOrder order, string? link,
                                           CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(approval.RequestedById)) return;

        var submitter = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == approval.RequestedById || u.Email == approval.RequestedById, ct);

        if (submitter?.Email is not { Length: > 0 }) return;

        var outcome = approval.Status == ApprovalStatus.Approved ? "approved" : "rejected";
        var subject = $"Purchase order {outcome} - {order.PoNumber}";

        var body = $"""
            Hi {DisplayName(submitter)},

            {order.PoNumber} has been {outcome} by {approval.DecidedByName ?? "the Financial Director"}.

            Supplier       {order.Supplier?.Name}
            Value          R {approval.AmountZarAtRequest:N2}
            Decided        {approval.DecidedAt?.ToLocalTime():dd MMM yyyy HH:mm}

            {(string.IsNullOrWhiteSpace(approval.DecisionReason) ? "" : $"Reason         {approval.DecisionReason}\n")}
            {(approval.Status == ApprovalStatus.Approved
                ? "You can now issue the purchase order."
                : "Make the changes asked for and send it for approval again.")}

            {(link is null ? "" : $"Full record: {link}")}
            """;

        // Deliberately not attached. The submitter raised this and has the document; what they need
        // is the decision and the reason.
        await SendAsync(approval, new EmailMessage(submitter.Email, DisplayName(submitter), subject, body, []), ct);
    }

    private async Task<List<ApplicationUser>> ActiveApproversAsync(CancellationToken ct)
    {
        var inRole = await _users.GetUsersInRoleAsync(Roles.FinancialDirector);

        return inRole
            .Where(u => u.IsActive && !string.IsNullOrWhiteSpace(u.Email))
            .OrderBy(u => u.Email)
            .ToList();
    }

    private async Task<List<EmailAttachment>> AttachmentsAsync(int orderId, CancellationToken ct)
    {
        var list = new List<EmailAttachment>();

        try
        {
            var rendered = await _documents.RenderAsync(orderId, ct);
            if (rendered is ({ } fileName, { } content) && content.LongLength <= MaxAttachmentBytes)
                list.Add(new EmailAttachment(fileName, "application/pdf", content));
        }
        catch (Exception ex)
        {
            // A document that will not render must not swallow the approval request. The body
            // carries the figures and a link, so the decision is still makeable.
            _log.LogError(ex, "Could not render the purchase order for order {OrderId} to attach.", orderId);
        }

        return list;
    }

    private async Task SendAsync(Approval approval, EmailMessage message, CancellationToken ct)
    {
        try
        {
            await _email.SendAsync(message, ct);
            approval.NotificationSent = true;
            approval.NotifiedAt = _clock.GetUtcNow().UtcDateTime;
            approval.NotificationError = null;
        }
        catch (EmailException ex)
        {
            approval.NotificationSent = false;
            approval.NotificationError = ex.Message;
        }
    }

    private static string ApproverBody(Approval approval, SupplierOrder order, ApplicationUser approver,
                                       string? link, bool attached)
    {
        var projects = string.Join(", ", order.OrderProjects
            .Select(op => op.Project?.Name).Where(n => !string.IsNullOrWhiteSpace(n)));

        return $"""
            Hi {DisplayName(approver)},

            {approval.RequestedByName ?? "A member of staff"} has asked you to approve a purchase order.

            PO number      {order.PoNumber}
            Supplier       {order.Supplier?.Name}
            Product        {order.ProductDescription}
            Project(s)     {(string.IsNullOrWhiteSpace(projects) ? "-" : projects)}

            Value          R {approval.AmountZarAtRequest:N2} ({order.InvoiceValueForeign:N2} {order.CurrencyCode} at {order.ExchangeRate:N6})
            {(string.IsNullOrWhiteSpace(approval.RequestNote) ? "" : $"Note           {approval.RequestNote}\n")}
            Approving commits Soft & Co to this amount. It is not an instruction to the supplier
            until it has been approved and issued.

            {(attached ? "The purchase order is attached." : "The purchase order is on the record below.")}

            {(link is null ? "" : $"Approve or reject: {link}")}
            """;
    }

    private static string DisplayName(ApplicationUser user) =>
        string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email ?? "" : user.DisplayName;
}
