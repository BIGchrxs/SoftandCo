using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftCo.Models;

/// <summary>
/// Money received against a client invoice.
///
/// Rows rather than columns on the invoice, for the reason <see cref="OrderPayment"/> is: clients
/// pay in instalments, at different times, by different means, and a pair of fixed "deposit" and
/// "balance" columns cannot represent three payments or a part payment honestly.
///
/// Receipts are <c>Restrict</c>-linked to their invoice. Recorded money must not be able to vanish
/// because somebody deleted the document it was received against.
/// </summary>
public class InvoiceReceipt
{
    public int Id { get; set; }

    public int CustomerInvoiceId { get; set; }
    public CustomerInvoice? CustomerInvoice { get; set; }

    [Column(TypeName = "numeric(18,2)")]
    public decimal AmountZar { get; set; }

    /// <summary>
    /// When the money actually arrived, which is not when somebody got round to recording it. Used
    /// for ageing, so it has to be the bank's date rather than the typist's.
    /// </summary>
    public DateOnly ReceivedDate { get; set; }

    public ReceiptMethod Method { get; set; } = ReceiptMethod.Eft;

    /// <summary>The bank reference, so a receipt can be matched to a statement line.</summary>
    [StringLength(200)]
    public string? Reference { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [StringLength(450)] public string? CreatedById { get; set; }
}
