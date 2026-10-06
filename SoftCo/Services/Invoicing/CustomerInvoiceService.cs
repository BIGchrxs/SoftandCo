using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SoftCo.Data;
using SoftCo.Models;
using SoftCo.Services.Approvals;
using SoftCo.Services.Numbering;
using SoftCo.Services.Pdf;

namespace SoftCo.Services.Invoicing;

/// <summary>One line as the form supplies it, before any arithmetic.</summary>
public sealed record InvoiceLineInput(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    VatTreatment VatTreatment);

public interface ICustomerInvoiceService
{
    /// <summary>
    /// Replaces a draft's lines and recalculates every total. The only path by which invoice
    /// amounts are ever written.
    /// </summary>
    Task<RuleResult> SaveLinesAsync(int invoiceId, IReadOnlyList<InvoiceLineInput> lines,
                                    bool pricesEnteredInclusive, CancellationToken ct = default);

    Task<RuleResult> SubmitAsync(int invoiceId, Actor actor, string? note, CancellationToken ct = default);
    Task<RuleResult> ApproveAsync(int approvalId, Actor actor, string? note, CancellationToken ct = default);
    Task<RuleResult> RejectAsync(int approvalId, Actor actor, string reason, CancellationToken ct = default);
    Task<RuleResult> WithdrawAsync(int invoiceId, Actor actor, CancellationToken ct = default);

    /// <summary>Allocates the number, dates the invoice, and makes it immutable.</summary>
    Task<RuleResult> IssueAsync(int invoiceId, Actor actor, CancellationToken ct = default);

    /// <summary>The VAT rate to apply to a treatment for a new line, from configuration.</summary>
    decimal RateFor(VatTreatment treatment);
}

/// <summary>
/// Everything that writes to a client invoice.
///
/// All arithmetic goes through <see cref="InvoiceMath"/> and nowhere else, so there is exactly one
/// answer to what a line comes to and the header is always the sum of the rows. The judgement about
/// what may happen when lives in <see cref="ApprovalRules"/>.
/// </summary>
public sealed class CustomerInvoiceService : ICustomerInvoiceService
{
    private readonly AppDbContext _db;
    private readonly IDocumentNumberGenerator _numbers;
    private readonly IAuditService _audit;
    private readonly CompanyOptions _company;
    private readonly TimeProvider _clock;

    public CustomerInvoiceService(AppDbContext db, IDocumentNumberGenerator numbers, IAuditService audit,
                                  IOptions<CompanyOptions> company, TimeProvider clock)
    {
        _db = db;
        _numbers = numbers;
        _audit = audit;
        _company = company.Value;
        _clock = clock;
    }

    /// <summary>
    /// The rate for a treatment, read from configuration for <b>new</b> lines only. Once a line is
    /// saved its rate is frozen on the row, so changing this never re-prices an existing invoice.
    /// </summary>
    public decimal RateFor(VatTreatment treatment) => treatment switch
    {
        VatTreatment.Standard => _company.IsVatRegistered ? _company.StandardVatRatePercent : 0m,
        _ => 0m
    };

    public async Task<RuleResult> SaveLinesAsync(int invoiceId, IReadOnlyList<InvoiceLineInput> lines,
                                                 bool pricesEnteredInclusive, CancellationToken ct = default)
    {
        var invoice = await _db.CustomerInvoices
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct);

        if (invoice is null) return RuleResult.Refuse("That invoice no longer exists.");

        var editable = ApprovalRules.CanEditInvoice(invoice.Status);
        if (!editable.Ok) return editable;

        _db.CustomerInvoiceLines.RemoveRange(invoice.Lines);
        invoice.Lines.Clear();

        var computed = new List<LineAmounts>();
        var sort = 0;

        foreach (var input in lines.Where(l => !string.IsNullOrWhiteSpace(l.Description)))
        {
            var rate = RateFor(input.VatTreatment);

            // Inclusive entry is converted here, once. Everything downstream - the stored line, the
            // totals, the PDF, gross profit - works in exclusive amounts.
            var unitExcl = pricesEnteredInclusive
                ? InvoiceMath.ExclusiveFromInclusive(input.UnitPrice, rate)
                : input.UnitPrice;

            var amounts = InvoiceMath.Line(input.Quantity, unitExcl, rate);
            computed.Add(amounts);

            invoice.Lines.Add(new CustomerInvoiceLine
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
        invoice.NetTotal = totals.NetTotal;
        invoice.VatTotal = totals.VatTotal;
        invoice.GrandTotal = totals.GrandTotal;
        invoice.PricesEnteredInclusive = pricesEnteredInclusive;

        await _db.SaveChangesAsync(ct);
        return RuleResult.Allowed;
    }

    public async Task<RuleResult> SubmitAsync(int invoiceId, Actor actor, string? note,
                                              CancellationToken ct = default)
    {
        var invoice = await LoadAsync(invoiceId, ct);
        if (invoice is null) return RuleResult.Refuse("That invoice no longer exists.");

        var check = ApprovalRules.CanSubmitInvoice(invoice.Status, invoice.ClientId > 0, invoice.Lines.Count);
        if (!check.Ok) return check;

        _db.Approvals.Add(new Approval
        {
            Kind = ApprovalKind.CustomerInvoice,
            Status = ApprovalStatus.Pending,
            CustomerInvoiceId = invoice.Id,
            AmountZarAtRequest = invoice.GrandTotal,
            RequestedAt = _clock.GetUtcNow().UtcDateTime,
            RequestedById = actor.Id,
            RequestedByName = actor.Name,
            RequestNote = ApprovalDecision.Trim(note)
        });

        invoice.Status = CustomerInvoiceStatus.PendingApproval;

        _audit.Record(nameof(CustomerInvoice), invoice.Id.ToString(), "InvoiceSubmitted",
                      newValue: $"R {invoice.GrandTotal:N2}");

        await _db.SaveChangesAsync(ct);
        return RuleResult.Allowed;
    }

    public async Task<RuleResult> ApproveAsync(int approvalId, Actor actor, string? note,
                                               CancellationToken ct = default)
    {
        var (approval, invoice, refusal) = await LoadForDecisionAsync(approvalId, actor, ct);
        if (refusal is not null) return refusal;

        ApprovalDecision.Record(approval!, ApprovalStatus.Approved, actor, note, _clock.GetUtcNow().UtcDateTime);
        invoice!.Status = CustomerInvoiceStatus.Approved;

        _audit.Record(nameof(CustomerInvoice), invoice.Id.ToString(), "InvoiceApproved",
                      newValue: $"R {approval!.AmountZarAtRequest:N2} by {actor.Name}",
                      reason: ApprovalDecision.Trim(note));

        await _db.SaveChangesAsync(ct);
        return RuleResult.Allowed;
    }

    public async Task<RuleResult> RejectAsync(int approvalId, Actor actor, string reason,
                                              CancellationToken ct = default)
    {
        var (approval, invoice, refusal) = await LoadForDecisionAsync(approvalId, actor, ct, reason);
        if (refusal is not null) return refusal;

        ApprovalDecision.Record(approval!, ApprovalStatus.Rejected, actor, reason, _clock.GetUtcNow().UtcDateTime);
        invoice!.Status = CustomerInvoiceStatus.Rejected;

        _audit.Record(nameof(CustomerInvoice), invoice.Id.ToString(), "InvoiceRejected",
                      newValue: actor.Name, reason: ApprovalDecision.Trim(reason));

        await _db.SaveChangesAsync(ct);
        return RuleResult.Allowed;
    }

    public async Task<RuleResult> WithdrawAsync(int invoiceId, Actor actor, CancellationToken ct = default)
    {
        var invoice = await LoadAsync(invoiceId, ct);
        if (invoice is null) return RuleResult.Refuse("That invoice no longer exists.");

        if (invoice.Status is not (CustomerInvoiceStatus.PendingApproval or CustomerInvoiceStatus.Approved))
            return RuleResult.Refuse("There is nothing to withdraw on this invoice.");

        var open = invoice.Approvals
            .Where(a => a.Status == ApprovalStatus.Pending)
            .OrderByDescending(a => a.RequestedAt)
            .FirstOrDefault();

        if (open is not null)
            ApprovalDecision.Record(open, ApprovalStatus.Withdrawn, actor,
                                    "Withdrawn by the submitter.", _clock.GetUtcNow().UtcDateTime);

        invoice.Status = CustomerInvoiceStatus.Draft;

        _audit.Record(nameof(CustomerInvoice), invoice.Id.ToString(), "InvoiceWithdrawn",
                      newValue: actor.Name);

        await _db.SaveChangesAsync(ct);
        return RuleResult.Allowed;
    }

    public async Task<RuleResult> IssueAsync(int invoiceId, Actor actor, CancellationToken ct = default)
    {
        var invoice = await _db.CustomerInvoices
            .Include(i => i.Client)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct);

        if (invoice is null) return RuleResult.Refuse("That invoice no longer exists.");

        var check = ApprovalRules.CanIssueInvoice(invoice.Status, invoice.GrandTotal);
        if (!check.Ok) return check;

        var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);

        // The number is allocated here and nowhere earlier: an abandoned draft must not burn a
        // number out of a series SARS expects to be unbroken.
        invoice.InvoiceNumber = await _numbers.NextAsync(DocumentNumberKind.CustomerInvoice, ct);
        invoice.IssueDate = today;
        invoice.DueDate = today.AddDays(invoice.PaymentTermsDays);
        invoice.Status = CustomerInvoiceStatus.Issued;
        invoice.IssuedById = actor.Id;
        invoice.IssuedByName = actor.Name;

        _audit.Record(nameof(CustomerInvoice), invoice.Id.ToString(), "InvoiceIssued",
                      newValue: $"{invoice.InvoiceNumber} R {invoice.GrandTotal:N2} by {actor.Name}");

        await _db.SaveChangesAsync(ct);
        return RuleResult.Allowed;
    }

    private Task<CustomerInvoice?> LoadAsync(int invoiceId, CancellationToken ct) =>
        _db.CustomerInvoices
           .Include(i => i.Lines)
           .Include(i => i.Approvals)
           .AsSplitQuery()
           .FirstOrDefaultAsync(i => i.Id == invoiceId, ct);

    private async Task<(Approval?, CustomerInvoice?, RuleResult?)> LoadForDecisionAsync(
        int approvalId, Actor actor, CancellationToken ct, string? rejectionReason = null)
    {
        var approval = await _db.Approvals.FirstOrDefaultAsync(a => a.Id == approvalId, ct);

        if (approval?.CustomerInvoiceId is not int invoiceId)
            return (null, null, RuleResult.Refuse("That approval request no longer exists."));

        var check = rejectionReason is null
            ? ApprovalRules.CanDecide(approval.Status, approval.RequestedById, actor.Id)
            : ApprovalRules.CanReject(approval.Status, approval.RequestedById, actor.Id, rejectionReason);

        if (!check.Ok) return (null, null, check);

        var invoice = await _db.CustomerInvoices.FirstOrDefaultAsync(i => i.Id == invoiceId, ct);

        return invoice is null
            ? (null, null, RuleResult.Refuse("That invoice no longer exists."))
            : (approval, invoice, null);
    }
}
