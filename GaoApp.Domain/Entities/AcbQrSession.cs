using System.ComponentModel.DataAnnotations;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

public enum AcbSessionStatus { Creating, Pending, Received, Completed, Cancelled, ReviewRequired }

/// <summary>Immutable routing and amount snapshot for one bank QR attempt.</summary>
public sealed class AcbQrSession : BaseStoreEntity
{
    public int QrRequestId { get; set; }
    public int OrderId { get; set; }
    public int ShiftId { get; set; }
    public int TerminalId { get; set; }
    public int CashierId { get; set; }
    [MaxLength(80)] public string ProviderOrderId { get; set; } = "";
    [MaxLength(100)] public string TraceNumber { get; set; } = "";
    [MaxLength(100)] public string VirtualAccount { get; set; } = "";
    [MaxLength(64)] public string CartFingerprint { get; set; } = "";
    public decimal Amount { get; set; }
    public AcbSessionStatus Status { get; set; }
    [MaxLength(500)] public string? ReviewReason { get; set; }
    public int? PaymentId { get; set; }
    public AcbConfirmationSource? ConfirmationSource { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public int? ConfirmedByUserId { get; set; }
    public int? ConfirmationCallbackReceiptId { get; set; }
    public DateTime? PrintClaimedAtUtc { get; set; }
    public DateTime? LastRetrievedAtUtc { get; set; }
    public string? LastRetrieveJson { get; set; }
}
