using System.ComponentModel.DataAnnotations;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.POSShifts;

public sealed class CreatePaymentAdjustmentRequest
{
    public Guid ClientRequestId { get; set; }
    [Required] public string RowVersion { get; set; } = "";
    [EnumDataType(typeof(PaymentMethod))] public PaymentMethod Method { get; set; }
    [StringLength(100)] public string? Reference { get; set; }
    [Required, StringLength(500)] public string RequestReason { get; set; } = "";
}
public sealed class PaymentAdjustmentDecision
{
    [Required] public string RowVersion { get; set; } = "";
    public string? ShiftRowVersion { get; set; }
    [StringLength(500)] public string? Note { get; set; }
}
public sealed record PaymentAdjustmentCandidateDto(int Id, int OrderId, string Number, int ShiftId, string? ShiftCode,
    string ShiftStatus, string Method, decimal Amount, string? Reference, DateTime PaidAtUtc, string RowVersion,
    bool CanRequest, string? UnavailableReason, int? PendingRequestId, int? DepositEntryId = null, int? CustomerId = null, string? CustomerName = null);
public sealed record PaymentAdjustmentPosition(decimal CashSales, decimal NonCashSales, decimal Expected,
    decimal? Actual, decimal? Received, decimal? Difference, bool NeedsReconciliation);
public sealed record PaymentAdjustmentItemDto(int Id, int PaymentId, int OrderId, string Number, int ShiftId,
    string? ShiftCode, string RequestedBy, DateTime CreatedAtUtc, string Status, string RequestReason, decimal Amount,
    string OldMethod, string NewMethod, string? OldReference, string? NewReference, decimal ExpectedDelta,
    string RowVersion, string? ReviewedBy, DateTime? ReviewedAtUtc, string? ReviewNote,
    string? BeforeShiftJson, string? AfterShiftJson, bool AppliedToClosedShift,
    DateTime? ReconciledAtUtc, string? ReconciledBy, string? ReconciliationNote, int? DepositEntryId = null, int? CustomerId = null, string? CustomerName = null);
public sealed record PaymentAdjustmentDetailDto(PaymentAdjustmentItemDto Request,
    PaymentAdjustmentPosition CurrentShift, PaymentAdjustmentPosition ProposedShift, string ShiftRowVersion,
    bool CanApprove, bool CanWithdraw, string? UnavailableReason);
public sealed record PaymentAdjustmentPreviewDto(PaymentAdjustmentPosition CurrentShift,
    PaymentAdjustmentPosition ProposedShift, decimal ExpectedDelta);
