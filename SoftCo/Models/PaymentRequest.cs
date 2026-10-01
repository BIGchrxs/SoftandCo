using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SoftCo.Models;

/// <summary>
/// A record that someone asked a named person to pay a specific invoice, at a specific time.
///
/// Written whether the send succeeds or fails. A failed attempt that left no trace would let
/// "I requested that payment" stand for a message that never arrived, which is precisely the
/// ambiguity this table exists to remove.
/// </summary>
public class PaymentRequest
{
    public int Id { get; set; }

    public int SupplierOrderId { get; set; }
    public SupplierOrder? SupplierOrder { get; set; }

    public int PaymentContactId { get; set; }
    public PaymentContact? PaymentContact { get; set; }

    public PaymentRequestStatus Status { get; set; } = PaymentRequestStatus.Sent;

    [StringLength(300)]
    public string? Subject { get; set; }

    /// <summary>Populated only on failure. Never contains credentials or the message body.</summary>
    [StringLength(1000)]
    public string? ErrorMessage { get; set; }

    /// <summary>Amount outstanding at the moment the request went out, in Rand.</summary>
    [Column(TypeName = "numeric(18,2)")]
    public decimal AmountZarAtRequest { get; set; }

    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    [StringLength(450)] public string? SentById { get; set; }
    [StringLength(256)] public string? SentByName { get; set; }
}
