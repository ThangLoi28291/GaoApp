using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

public enum POSCashAdjustmentStatus { Pending = 0, Approved = 1, Rejected = 2, Withdrawn = 3 }

public class POSCashAdjustmentRequest : BaseStoreEntity, IAuditTrackedEntity
{
    public Guid ClientRequestId { get; set; }
    public int TransactionId { get; set; }
    public POSShiftCashTransaction Transaction { get; set; } = default!;
    public int POSShiftId { get; set; }
    public POSShift POSShift { get; set; } = default!;
    public int RequestedByUserId { get; set; }
    [MaxLength(200)] public string RequestedByName { get; set; } = "";
    public bool IsCancellation { get; set; }
    public POSCashAdjustmentStatus Status { get; set; }
    [MaxLength(500)] public string RequestReason { get; set; } = "";
    [MaxLength(32)] public string TransactionVersion { get; set; } = "";
    public POSShiftCashTransactionType OldType { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal OldAmount { get; set; }
    [MaxLength(300)] public string OldReason { get; set; } = "";
    [MaxLength(500)] public string? OldNote { get; set; }
    public POSShiftCashTransactionType NewType { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal NewAmount { get; set; }
    [MaxLength(300)] public string NewReason { get; set; } = "";
    [MaxLength(500)] public string? NewNote { get; set; }
    public int? ReviewedByUserId { get; set; }
    [MaxLength(200)] public string? ReviewedByName { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    [MaxLength(500)] public string? ReviewNote { get; set; }
    public string? BeforeShiftJson { get; set; }
    public string? AfterShiftJson { get; set; }
    public bool AppliedToClosedShift { get; set; }
    public DateTime? ReconciledAtUtc { get; set; }
    public int? ReconciledByUserId { get; set; }
    [MaxLength(200)] public string? ReconciledByName { get; set; }
    [MaxLength(500)] public string? ReconciliationNote { get; set; }
}
