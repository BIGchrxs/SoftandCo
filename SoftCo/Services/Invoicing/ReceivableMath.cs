using SoftCo.Models;
using SoftCo.Services.Approvals;

namespace SoftCo.Services.Invoicing;

/// <summary>
/// What a client still owes, and whether staff may change it.
///
/// Pure and written against its tests first, like <see cref="InvoiceMath"/>. This is the other end
/// of the same problem: <c>InvoiceMath</c> decides what was charged, this decides what is left, and
/// between them they are the only places a receivable balance is ever computed.
///
/// There are exactly two ways money can be made to disappear from this ledger - a receipt and a
/// credit note - and both are gated here.
/// </summary>
public static class ReceivableMath
{
    /// <summary>
    /// What is actually owed, after credit notes.
    ///
    /// Credits come off before receipts because a credit note cancels part of the charge; it is not
    /// a payment, and treating it as one would make an invoice look settled by money that never
    /// arrived.
    /// </summary>
    public static decimal AmountDue(decimal grandTotal, decimal creditedTotal) =>
        InvoiceMath.Round(grandTotal - creditedTotal);

    /// <summary>
    /// The balance still to be collected.
    ///
    /// Negative when a client has overpaid. Deliberately not clamped to zero: an overpayment is
    /// money Soft &amp; Co owe back, and hiding it behind a zero is how it never gets refunded.
    /// </summary>
    public static decimal Outstanding(decimal grandTotal, decimal creditedTotal, decimal receivedTotal) =>
        InvoiceMath.Round(AmountDue(grandTotal, creditedTotal) - receivedTotal);

    /// <summary>What is left before credits would exceed what was ever charged.</summary>
    public static decimal CreditHeadroom(decimal grandTotal, decimal creditedTotal) =>
        Math.Max(0m, InvoiceMath.Round(grandTotal - creditedTotal));

    /// <summary>
    /// The status an invoice should carry, given what has been received and credited.
    ///
    /// Derived, never typed in - the discipline <see cref="SettlementStatus"/> already applies to
    /// supplier orders. A status somebody maintains by hand drifts out of step with the money beside
    /// it, which is the spreadsheet failure this system exists to remove.
    ///
    /// Only invoices that are actually receivable are reclassified. A draft, a rejected invoice or a
    /// cancelled one is returned untouched: none of them is money anybody owes.
    /// </summary>
    public static CustomerInvoiceStatus StatusFor(CustomerInvoiceStatus current, decimal grandTotal,
                                                  decimal creditedTotal, decimal receivedTotal)
    {
        if (current is not (CustomerInvoiceStatus.Issued or CustomerInvoiceStatus.PartPaid
                                                        or CustomerInvoiceStatus.Paid))
            return current;

        var outstanding = Outstanding(grandTotal, creditedTotal, receivedTotal);

        // Settled covers both "paid in full" and "credited away entirely" - a cancelled charge is
        // not an outstanding one, and leaving it on the receivables list forever helps nobody.
        if (outstanding <= 0m) return CustomerInvoiceStatus.Paid;

        return receivedTotal > 0m ? CustomerInvoiceStatus.PartPaid : CustomerInvoiceStatus.Issued;
    }

    /// <summary>
    /// Whether a receipt may be recorded.
    ///
    /// An overpayment is allowed through on purpose. Clients do pay too much, and refusing to record
    /// it means the books stop matching the bank statement - which is a worse problem than the one
    /// being prevented. It is flagged on screen instead.
    /// </summary>
    public static RuleResult CanRecordReceipt(decimal amount, decimal outstanding)
    {
        if (amount <= 0m)
            return RuleResult.Refuse("A receipt has to be for more than nothing. " +
                                     "To reduce what is owed, raise a credit note instead.");

        if (outstanding <= 0m)
            return RuleResult.Refuse("There is nothing outstanding on this invoice.");

        return RuleResult.Allowed;
    }

    /// <summary>
    /// Whether a credit note of this value may be issued.
    ///
    /// The cap is the point of the method: crediting more than was ever charged manufactures a
    /// refund out of nothing, and does it in a document a tax authority will read.
    /// </summary>
    public static RuleResult CanCredit(decimal amount, decimal grandTotal, decimal alreadyCredited)
    {
        if (amount <= 0m)
            return RuleResult.Refuse("A credit note has to be for more than nothing.");

        var headroom = CreditHeadroom(grandTotal, alreadyCredited);

        if (headroom <= 0m)
            return RuleResult.Refuse("This invoice has already been credited in full.");

        if (amount > headroom)
            return RuleResult.Refuse(
                $"That is more than is left to credit. The invoice totals R {grandTotal:N2}, " +
                $"R {alreadyCredited:N2} has already been credited, so at most R {headroom:N2} remains.");

        return RuleResult.Allowed;
    }
}
