using System.ComponentModel.DataAnnotations;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.POSShifts;

public sealed class CreateCashAdjustmentRequest
{
    public Guid ClientRequestId { get; set; }
    [Required] public string RowVersion { get; set; } = "";
    public bool IsCancellation { get; set; }
    [EnumDataType(typeof(POSShiftCashTransactionType))] public POSShiftCashTransactionType Type { get; set; }
    public decimal Amount { get; set; }
    [StringLength(300)] public string? Reason { get; set; }
    [StringLength(500)] public string? Note { get; set; }
    [Required, StringLength(500)] public string RequestReason { get; set; } = "";
}
public sealed class CashAdjustmentDecision
{
    [Required] public string RowVersion { get; set; } = "";
    [StringLength(500)] public string? Note { get; set; }
}
public sealed record CashAdjustmentValues(string Type, decimal Amount, string Reason, string? Note);
public sealed record ShiftCashPosition(decimal CashInTotal, decimal CashOutTotal, decimal Expected,
    decimal? Actual, decimal? Received, decimal? Difference, decimal? ReceivedDifference, bool NeedsReconciliation);
public sealed record CashAdjustmentTransactionDto(int Id, int ShiftId, string? ShiftCode, string? Terminal,
    string ShiftStatus, bool Cancelled, CashAdjustmentValues Values, DateTime CreatedAtUtc, string RowVersion, int? PendingRequestId);
public sealed record CashAdjustmentItemDto(int Id, int TransactionId, int ShiftId, string? ShiftCode, string? Terminal,
    string RequestedBy, DateTime CreatedAtUtc, string Status, bool IsCancellation, string RequestReason,
    CashAdjustmentValues Before, CashAdjustmentValues After, decimal ExpectedDelta, string RowVersion,
    string? ReviewedBy, DateTime? ReviewedAtUtc, string? ReviewNote, string? BeforeShiftJson, string? AfterShiftJson,
    bool AppliedToClosedShift, DateTime? ReconciledAtUtc, string? ReconciledBy, string? ReconciliationNote);
public sealed record CashAdjustmentDetailDto(CashAdjustmentItemDto Request, ShiftCashPosition CurrentShift,
    ShiftCashPosition ProposedShift, string ShiftRowVersion, bool CanApprove, bool CanWithdraw);
