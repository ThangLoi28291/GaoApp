using System.ComponentModel.DataAnnotations;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

// The authenticated bank request is committed before HTTP acknowledgement.
public sealed class AcbCallbackReceipt : BaseStoreEntity
{
    [MaxLength(36)] public string ClientRequestId { get; set; } = "";
    public int Page { get; set; }
    [MaxLength(30)] public string RequestCode { get; set; } = "TRANSACTION_UPDATE";
    public int TotalPages { get; set; } = 1;
    public List<AcbQrNotificationItem> Items { get; set; } = [];
    [MaxLength(64)] public string PayloadHash { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    public int Attempts { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public bool NeedsReview { get; set; }
    [MaxLength(100)] public string? LastErrorCode { get; set; }
}
