using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

public sealed class POSPaymentAdjustmentRequest : BaseStoreEntity, IAuditTrackedEntity
{
    public Guid ClientRequestId { get; set; }
    public int? PaymentId { get; set; }
    public OrderPayment? Payment { get; set; }
    public int? OrderId { get; set; }
    public Order? Order { get; set; }
    public int? DepositEntryId { get; set; }
    public CustomerDepositEntry? DepositEntry { get; set; }
    public int? OldStoreBankAccountId { get; set; }
    public int? NewStoreBankAccountId { get; set; }
    public int? CashTransactionId { get; set; }
    [MaxLength(32)] public string? CashTransactionVersion { get; set; }
    public int POSShiftId { get; set; }
    public POSShift POSShift { get; set; } = default!;
    public int RequestedByUserId { get; set; }
    [MaxLength(200)] public string RequestedByName { get; set; } = "";
    public POSCashAdjustmentStatus Status { get; set; }
    [MaxLength(500)] public string RequestReason { get; set; } = "";
    [MaxLength(32)] public string PaymentVersion { get; set; } = "";
    [MaxLength(32)] public string OrderVersion { get; set; } = "";
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
    public PaymentMethod OldMethod { get; set; }
    public PaymentMethod NewMethod { get; set; }
    [MaxLength(100)] public string? OldReference { get; set; }
    [MaxLength(100)] public string? NewReference { get; set; }
    [MaxLength(50)] public string? OldProvider { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal ExpectedDelta { get; set; }
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
