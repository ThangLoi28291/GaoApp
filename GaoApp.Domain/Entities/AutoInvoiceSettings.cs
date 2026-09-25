using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

[Table("AutoInvoiceSettings")]
public sealed class AutoInvoiceSettings : BaseStoreEntity, IAuditTrackedEntity
{
    public bool IsEnabled { get; set; }

    public int MinimumAgeMinutes { get; set; } = 30;

    [Column(TypeName = "decimal(18,2)")]
    public decimal SeparateAmountThreshold { get; set; } = 100_000m;

    [Column(TypeName = "decimal(18,2)")]
    public decimal GroupTargetAmount { get; set; } = 100_000m;

    public int SendIntervalSeconds { get; set; } = 60;

    public TimeSpan ClosingTimeLocal { get; set; } = new(23, 0, 0);

    public bool IssueOldDayRemainder { get; set; } = true;

    public AutoInvoiceScopeMode ScopeMode { get; set; } = AutoInvoiceScopeMode.Today;

    public DateTime? ScopeStartDateLocal { get; set; }

    public DateTime? ScopeEndDateLocal { get; set; }

    [StringLength(100)]
    public string TimeZoneId { get; set; } = "SE Asia Standard Time";

    public DateTime? LastEnabledAtUtc { get; set; }

    public DateTime? LastDisabledAtUtc { get; set; }
}
