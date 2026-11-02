using System.ComponentModel.DataAnnotations;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

public sealed class BankNotificationDevice : BaseStoreEntity, IAuditTrackedEntity
{
    public int BankAccountId { get; set; }
    [MaxLength(64)] public string BankAccountFingerprint { get; set; } = "";
    [MaxLength(120)] public string Name { get; set; } = "";
    [MaxLength(64)] public string TokenHash { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public DateTime? LastSeenAtUtc { get; set; }
}

public sealed class BankNotificationWait : BaseStoreEntity
{
    public int QrId { get; set; }
    public int OrderId { get; set; }
    public int BankAccountId { get; set; }
    [MaxLength(64)] public string BankAccountFingerprint { get; set; } = "";
    public int TerminalId { get; set; }
    public int ShiftId { get; set; }
    public decimal Amount { get; set; }
    [MaxLength(64)] public string CartFingerprint { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
}

public sealed class BankNotificationReceipt : BaseStoreEntity
{
    public int DeviceId { get; set; }
    public int BankAccountId { get; set; }
    [MaxLength(64)] public string EvidenceKey { get; set; } = "";
    [MaxLength(64)] public string PayloadHash { get; set; } = "";
    public string Content { get; set; } = "";
    public decimal Amount { get; set; }
    public int? OrderId { get; set; }
    [MaxLength(80)] public string? QrCode { get; set; }
    [MaxLength(240)] public string? SenderName { get; set; }
    [MaxLength(80)] public string TransactionCode { get; set; } = "";
    public DateTime PostedAtUtc { get; set; }
    public int? AppliedWaitId { get; set; }
    public int? PaymentId { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public int? ConfirmedByUserId { get; set; }
    [MaxLength(40)] public string? ConfirmationSource { get; set; }
}

public sealed class BankNotificationDismissal : BaseStoreEntity
{
    public int ReceiptId { get; set; }
    public int WaitId { get; set; }
}
