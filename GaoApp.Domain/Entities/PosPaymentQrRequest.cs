using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

[Table("PosPaymentQrRequests")]
public class PosPaymentQrRequest : BaseStoreEntity
{
    public int OrderId { get; set; }
    public Order Order { get; set; } = default!;

    public int BankAccountId { get; set; }
    public StoreBankAccount BankAccount { get; set; } = default!;

    public decimal Amount { get; set; }
    public Guid? ClientRequestId { get; set; }
    public int? PaymentId { get; set; }
    public DateTime? PrintClaimedAtUtc { get; set; }

    [StringLength(300)]
    public string Content { get; set; } = string.Empty;

    public BankQrRenderMode QrRenderMode { get; set; }

    public BankQrConfirmMode ConfirmMode { get; set; }

    public PosPaymentQrStatus Status { get; set; } = PosPaymentQrStatus.Pending;

    [StringLength(80)]
    public string RequestCode { get; set; } = string.Empty;
    // VD: QR-20260508-000001

    [StringLength(200)]
    public string? ProviderTransactionId { get; set; }

    public string? QrDataUrl { get; set; }
    // base64 image nếu render local

    public string? QrRawText { get; set; }
    // chuỗi EMV VietQR

    public string? ProviderRawResponseJson { get; set; }

    public string? CallbackRawJson { get; set; }

    public DateTime ExpireAtUtc { get; set; }

    public DateTime? PaidAtUtc { get; set; }

    public int? ManualConfirmedByUserId { get; set; }

    public DateTime? ManualConfirmedAtUtc { get; set; }
}
