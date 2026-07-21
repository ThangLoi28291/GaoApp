using System.ComponentModel.DataAnnotations;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Audit bất biến cho mỗi lần bật/tắt Multi LegalEntity ở cấp Store.
/// </summary>
public sealed class LegalEntityActivationEvent : BaseStoreEntity
{
    public LegalEntityActivationAction Action { get; set; }
    public bool PreviousIsEnabled { get; set; }
    public bool NewIsEnabled { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public DateTime? ActivationAtUtc { get; set; }

    public int? ChangedByUserId { get; set; }

    [StringLength(200)]
    public string? ChangedByUserName { get; set; }

    [Required]
    [StringLength(500)]
    public string Reason { get; set; } = string.Empty;

    public bool PreflightPassed { get; set; }

    /// <summary>
    /// Snapshot checks ở thời điểm đổi trạng thái để audit không phụ thuộc cấu hình sau này.
    /// </summary>
    public string? PreflightSnapshotJson { get; set; }
}
