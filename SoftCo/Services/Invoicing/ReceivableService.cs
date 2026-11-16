using Microsoft.EntityFrameworkCore;
using SoftCo.Data;
using SoftCo.Models;
using SoftCo.Services.Approvals;
using SoftCo.Services.Numbering;

namespace SoftCo.Services.Invoicing;

public interface IReceivableService
{
    Task<RuleResult> RecordReceiptAsync(int invoiceId, decimal amountZar, DateOnly receivedDate,
                                        ReceiptMethod method, string? reference, string? notes,
                                        Actor actor, CancellationToken ct = default);

    /// <summary>Removes a receipt recorded in error and puts the invoice's status back where it belongs.</summary>
    Task<RuleResult> RemoveReceiptAsync(int receiptId, Actor actor, CancellationToken ct = default);

    /// <summary>
    /// Creates a draft credit note against an issued invoice. Nothing is credited until it is
    /// issued.
    /// </summary>
    Task<(RuleResult Result, CreditNote? Note)> DraftCreditNoteAsync(
        int invoiceId, string reason, IReadOnlyList<InvoiceLineInput> lines, bool pricesEnteredInclusive,
        Actor actor, CancellationToken ct = default);

    // There is deliberately no IssueCreditNoteAsync. A credit note is issued by being approved and
    // in no other way, so there is no method anyone can reach that credits an invoice unsupervised.

    /// <summary>
    /// Puts a credit note to the Financial Director. Credits nothing by itself.
    /// </summary>
    Task<RuleResult> SubmitCreditNoteAsync(int creditNoteId, Actor actor, string? note,
                                           CancellationToken ct = default);

    /// <summary>
    /// Approves a credit note, which issues it in the same breath - the decision is the
    /// authorisation, and there is no second step for staff to forget.
    /// </summary>
    Task<RuleResult> ApproveCreditNoteAsync(int approvalId, Actor actor, string? note,
                                            CancellationToken ct = default);

    /// <summary>Refuses a credit note. Nothing is credited and the reason is kept against it.</summary>
    Task<RuleResult> RejectCreditNoteAsync(int approvalId, Actor actor, string reason,
                                           CancellationToken ct = default);

    /// <summary>Takes back a request before it is decided, cancelling the credit note with it.</summary>
    Task<RuleResult> WithdrawCreditNoteAsync(int creditNoteId, Actor actor,
                                             CancellationToken ct = default);
}

/// <summary>
/// The two ways money leaves a client's balance: a receipt, and a credit note.
///
/// Both are gated by <see cref="ReceivableMath"/>, and the invoice's status is recomputed from the
/// rows afterwards rather than set by hand - so a status can never claim something the receipts and
/// credits beside it do not support.
/// </summary>
public sealed class ReceivableService : IReceivableService
{
    private readonly AppDbContext _db;
    private readonly IDocumentNumberGenerator _numbers;
    private readonly ICustomerInvoiceService _invoices;
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;

    public ReceivableService(AppDbContext db, IDocumentNumberGenerator numbers,
                             ICustomerInvoiceService invoices, IAuditService audit, TimeProvider clock)
    {
        _db = db;
        _numbers = numbers;
        _invoices = invoices;
        _audit = audit;
        _clock = clock;
    }

    public async Task<RuleResult> RecordReceiptAsync(int invoiceId, decimal amountZar, DateOnly receivedDate,
                                                     ReceiptMethod method, string? reference, string? notes,
                                                     Actor actor, CancellationToken ct = default)
    {
        var invoice = await LoadAsync(invoiceId, ct);
        if (invoice is null) return RuleResult.Refuse("That invoice no longer exists.");

        if (!invoice.IsIssued)
            return RuleResult.Refuse("Nothing can be received against an invoice that has not been issued.");

        var check = ReceivableMath.CanRecordReceipt(amountZar, invoice.OutstandingZar);
        if (!check.Ok) return check;

        var rounded = InvoiceMath.Round(amountZar);

        _db.InvoiceReceipts.Add(new InvoiceReceipt
        {
            CustomerInvoiceId = invoice.Id,
            AmountZar = rounded,
            ReceivedDate = receivedDate,
            Method = method,
            Reference = Trim(reference, 200),
            Notes = Trim(notes, 1000),
            CreatedById = actor.Name
        });

        // The new total is passed in rather than read back off the invoice. `invoice` is tracked, so
        // adding a stand-in receipt to its collection just to make the sum come out right would make
        // EF insert a second row - every receipt recorded twice, silently.
        Restatus(invoice, invoice.CreditedTotal, invoice.ReceivedTotal + rounded);

        _audit.Record(nameof(CustomerInvoice), invoice.Id.ToString(), "ReceiptRecorded",
                      newValue: $"R {amountZar:N2} on {receivedDate:dd MMM yyyy} ({method.Label()})");

        await _db.SaveChangesAsync(ct);
        return RuleResult.Allowed;
    }

    public async Task<RuleResult> RemoveReceiptAsync(int receiptId, Actor actor, CancellationToken ct = default)
    {
        var receipt = await _db.InvoiceReceipts.FirstOrDefaultAsync(r => r.Id == receiptId, ct);
        if (receipt is null) return RuleResult.Refuse("That receipt no longer exists.");

        var invoice = await LoadAsync(receipt.CustomerInvoiceId, ct);
        if (invoice is null) return RuleResult.Refuse("That invoice no longer exists.");

        _db.InvoiceReceipts.Remove(receipt);

        Restatus(invoice, invoice.CreditedTotal, invoice.ReceivedTotal - receipt.AmountZar);

        // The amount is in the audit trail precisely because the row is going: deleting money that
        // leaves no trace is how a reconciliation becomes unanswerable.
        _audit.Record(nameof(CustomerInvoice), invoice.Id.ToString(), "ReceiptRemoved",
                      oldValue: $"R {receipt.AmountZar:N2} on {receipt.ReceivedDate:dd MMM yyyy}");

        await _db.SaveChangesAsync(ct);
        return RuleResult.Allowed;
    }

    public async Task<(RuleResult, CreditNote?)> DraftCreditNoteAsync(
        int invoiceId, string reason, IReadOnlyList<InvoiceLineInput> lines, bool pricesEnteredInclusive,
        Actor actor, CancellationToken ct = default)
    {
        var invoice = await LoadAsync(invoiceId, ct);
        if (invoice is null) return (RuleResult.Refuse("That invoice no longer exists."), null);

        if (!invoice.IsIssued)
            return (RuleResult.Refuse(
                "Only an issued invoice can be credited. A draft can simply be edited."), null);

        var trimmedReason = (reason ?? "").Trim();

        if (trimmedReason.Length < MinimumReasonLength)
            return (RuleResult.Refuse(
                "Say why this is being credited. A reduction in revenue with no reason is the first " +
                "thing an auditor asks about."), null);

        var note = new CreditNote
        {
            CustomerInvoiceId = invoice.Id,
            Status = CreditNoteStatus.Draft,
            Reason = trimmedReason,
            CreatedById = actor.Name
        };

        var computed = new List<LineAmounts>();
        var sort = 0;

        foreach (var input in lines.Where(l => !string.IsNullOrWhiteSpace(l.Description)))
        {
            // The rate comes from the invoice being credited where its lines carry one, not from
            // today's configuration. A 2026 invoice credited in 2028 must reverse at the rate it was
            // issued at, or the VAT returns on either side will not agree.
            var rate = RateForCredit(invoice, input.VatTreatment);

            var unitExcl = pricesEnteredInclusive
                ? InvoiceMath.ExclusiveFromInclusive(input.UnitPrice, rate)
                : input.UnitPrice;

            var amounts = InvoiceMath.Line(input.Quantity, unitExcl, rate);
            computed.Add(amounts);

            note.Lines.Add(new CreditNoteLine
            {
                Sort = sort++,
                Description = input.Description.Trim(),
                Quantity = input.Quantity,
                UnitPriceExclVat = unitExcl,
                VatTreatment = input.VatTreatment,
                VatRatePercent = rate,
                LineNetExclVat = amounts.NetExclVat,
                LineVatAmount = amounts.VatAmount,
                LineTotalInclVat = amounts.TotalInclVat
            });
        }

        var totals = InvoiceMath.Totals(computed);
        note.NetTotal = totals.NetTotal;
        note.VatTotal = totals.VatTotal;
        note.GrandTotal = totals.GrandTotal;

        // Checked at draft as well as at issue, so somebody is told now rather than after typing
        // out a full credit note.
        var check = ReceivableMath.CanCredit(note.GrandTotal, invoice.GrandTotal, invoice.CreditedTotal);
        if (!check.Ok) return (check, null);

        _db.CreditNotes.Add(note);
        await _db.SaveChangesAsync(ct);

        return (RuleResult.Allowed, note);
    }

    // --- Credit note approval ----------------------------------------------------------------
    // A credit note can take an invoice to nil, so it goes past the Financial Director like a
    // purchase order or an invoice does. Approving issues it: there is no approved-but-unissued
    // state in which the invoice looks credited and is not.

    public async Task<RuleResult> SubmitCreditNoteAsync(int creditNoteId, Actor actor, string? note,
                                                        CancellationToken ct = default)
    {
        var credit = await _db.CreditNotes
            .Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.Id == creditNoteId, ct);

        if (credit is null) return RuleResult.Refuse("That credit note no longer exists.");

        var check = ApprovalRules.CanSubmitCreditNote(credit.Status, credit.Lines.Count, credit.Reason);
        if (!check.Ok) return check;

        _db.Approvals.Add(new Approval
        {
            Kind = ApprovalKind.CreditNote,
            Status = ApprovalStatus.Pending,
            CreditNoteId = credit.Id,
            AmountZarAtRequest = credit.GrandTotal,
            RequestedAt = _clock.GetUtcNow().UtcDateTime,
            RequestedById = actor.Id,
            RequestedByName = actor.Name,
            RequestNote = ApprovalDecision.Trim(note)
        });

        credit.Status = CreditNoteStatus.PendingApproval;

        _audit.Record(nameof(CustomerInvoice), credit.CustomerInvoiceId.ToString(),
                      "CreditNoteSubmitted",
                      newValue: $"R {credit.GrandTotal:N2} by {actor.Name}", reason: credit.Reason);

        await _db.SaveChangesAsync(ct);
        return RuleResult.Allowed;
    }

    public async Task<RuleResult> ApproveCreditNoteAsync(int approvalId, Actor actor, string? note,
                                                         CancellationToken ct = default)
    {
        var (approval, credit, refusal) = await LoadCreditForDecisionAsync(approvalId, actor, ct);
        if (refusal is not null) return refusal;

        var allowed = ApprovalRules.CanIssueCreditNote(credit!.Status);
        if (!allowed.Ok) return allowed;

        var invoice = await LoadAsync(credit.CustomerInvoiceId, ct);
        if (invoice is null) return RuleResult.Refuse("That invoice no longer exists.");

        // The headroom is checked here, not only when the credit was raised: another credit note
        // may have been issued against this invoice while this one waited. Refusing before the
        // decision is recorded means the approval never claims to have allowed an over-credit.
        var room = ReceivableMath.CanCredit(credit.GrandTotal, invoice.GrandTotal, invoice.CreditedTotal);
        if (!room.Ok) return room;

        ApprovalDecision.Record(approval!, ApprovalStatus.Approved, actor, note,
                                _clock.GetUtcNow().UtcDateTime);

        credit.CreditNoteNumber = await _numbers.NextAsync(DocumentNumberKind.CreditNote, ct);
        credit.IssueDate = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        credit.Status = CreditNoteStatus.Issued;
        credit.IssuedById = actor.Id;
        credit.IssuedByName = actor.Name;

        Restatus(invoice, invoice.CreditedTotal + credit.GrandTotal, invoice.ReceivedTotal);

        _audit.Record(nameof(CustomerInvoice), invoice.Id.ToString(), "CreditNoteIssued",
                      newValue: $"{credit.CreditNoteNumber} R {credit.GrandTotal:N2} approved by {actor.Name}",
                      reason: credit.Reason);

        // The decision and the issue are one SaveChanges: an approval recorded without the credit
        // note it authorised, or a credit note issued with no approval behind it, are both worse
        // than the whole thing failing.
        await _db.SaveChangesAsync(ct);
        return RuleResult.Allowed;
    }

    public async Task<RuleResult> RejectCreditNoteAsync(int approvalId, Actor actor, string reason,
                                                        CancellationToken ct = default)
    {
        var (approval, credit, refusal) = await LoadCreditForDecisionAsync(approvalId, actor, ct, reason);
        if (refusal is not null) return refusal;

        ApprovalDecision.Record(approval!, ApprovalStatus.Rejected, actor, reason,
                                _clock.GetUtcNow().UtcDateTime);

        credit!.Status = CreditNoteStatus.Rejected;

        _audit.Record(nameof(CustomerInvoice), credit.CustomerInvoiceId.ToString(),
                      "CreditNoteRejected",
                      newValue: $"R {credit.GrandTotal:N2} refused by {actor.Name}",
                      reason: ApprovalDecision.Trim(reason));

        await _db.SaveChangesAsync(ct);
        return RuleResult.Allowed;
    }

    public async Task<RuleResult> WithdrawCreditNoteAsync(int creditNoteId, Actor actor,
                                                          CancellationToken ct = default)
    {
        var credit = await _db.CreditNotes
            .Include(c => c.Approvals)
            .FirstOrDefaultAsync(c => c.Id == creditNoteId, ct);

        if (credit is null) return RuleResult.Refuse("That credit note no longer exists.");

        if (credit.Status != CreditNoteStatus.PendingApproval)
            return RuleResult.Refuse("There is nothing to withdraw on this credit note.");

        if (credit.OpenApproval is { } open)
            ApprovalDecision.Record(open, ApprovalStatus.Withdrawn, actor, null,
                                    _clock.GetUtcNow().UtcDateTime);

        // Cancelled rather than back to draft: a credit note carries no edit screen, so a draft
        // would sit on the invoice forever with nothing anyone could do to it. Raise a new one.
        credit.Status = CreditNoteStatus.Cancelled;

        _audit.Record(nameof(CustomerInvoice), credit.CustomerInvoiceId.ToString(),
                      "CreditNoteWithdrawn",
                      newValue: $"R {credit.GrandTotal:N2} withdrawn by {actor.Name}");

        await _db.SaveChangesAsync(ct);
        return RuleResult.Allowed;
    }

    private async Task<(Approval?, CreditNote?, RuleResult?)> LoadCreditForDecisionAsync(
        int approvalId, Actor actor, CancellationToken ct, string? rejectionReason = null)
    {
        var approval = await _db.Approvals.FirstOrDefaultAsync(a => a.Id == approvalId, ct);

        if (approval?.CreditNoteId is not int creditNoteId)
            return (null, null, RuleResult.Refuse("That approval request no longer exists."));

        var check = rejectionReason is null
            ? ApprovalRules.CanDecide(approval.Status, approval.RequestedById, actor.Id)
            : ApprovalRules.CanReject(approval.Status, approval.RequestedById, actor.Id, rejectionReason);

        if (!check.Ok) return (null, null, check);

        var credit = await _db.CreditNotes
            .Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.Id == creditNoteId, ct);

        return credit is null
            ? (null, null, RuleResult.Refuse("That credit note no longer exists."))
            : (approval, credit, null);
    }

    /// <summary>A credit note's reason has to be actionable, like a rejection's.</summary>
    public const int MinimumReasonLength = 10;

    /// <summary>
    /// The rate to reverse at. Taken from a line on the invoice carrying the same treatment, so the
    /// credit matches what was charged; only when the invoice has no such line does it fall back to
    /// today's configured rate.
    /// </summary>
    private decimal RateForCredit(CustomerInvoice invoice, VatTreatment treatment)
    {
        var onInvoice = invoice.Lines.FirstOrDefault(l => l.VatTreatment == treatment);
        return onInvoice?.VatRatePercent ?? _invoices.RateFor(treatment);
    }

    /// <summary>
    /// Recomputes the invoice's status. Never set by hand anywhere else.
    ///
    /// Takes the new totals explicitly because the caller is mid-change: the receipt or credit that
    /// prompted this is in the change tracker, not yet in the invoice's loaded collections.
    /// </summary>
    private static void Restatus(CustomerInvoice invoice, decimal credited, decimal received) =>
        invoice.Status = ReceivableMath.StatusFor(invoice.Status, invoice.GrandTotal, credited, received);

    private Task<CustomerInvoice?> LoadAsync(int invoiceId, CancellationToken ct) =>
        _db.CustomerInvoices
           .Include(i => i.Lines)
           .Include(i => i.Receipts)
           .Include(i => i.CreditNotes)
           .AsSplitQuery()
           .FirstOrDefaultAsync(i => i.Id == invoiceId, ct);

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }
}
