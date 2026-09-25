using System.ComponentModel.DataAnnotations;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

// Notification evidence, never an OrderPayment. Bank retrieval remains authoritative.
public sealed class AcbQrNotificationItem : BaseStoreEntity
{
    public int ReceiptId { get; set; }
    public AcbCallbackReceipt Receipt { get; set; } = default!;
    public int Position { get; set; }
    [MaxLength(300)] public string ProviderOrderId { get; set; } = "";
    [MaxLength(30)] public string RequestCode { get; set; } = "";
    public DateTime BusinessDate { get; set; }
    public decimal Amount { get; set; }
    [MaxLength(30)] public string TransactionStatus { get; set; } = "";
    [MaxLength(10)] public string DebitOrCredit { get; set; } = "";
    public string Content { get; set; } = "";
}
