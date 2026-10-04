namespace GaoApp.Application.DTOs.POSShifts;

public sealed class POSShiftReconciliationDto
{
    public int StoreId { get; set; }
    public int ShiftId { get; set; }
    public int OwnerId { get; set; }
    public string OwnerName { get; set; } = "";
    public int TerminalId { get; set; }
    public string TerminalName { get; set; } = "";
    public string ShiftCode { get; set; } = "";
    public string Status { get; set; } = "";
    public bool CanReturnToClose { get; set; }
    public DateTime OpenedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public DateTime AsOfUtc { get; set; }
    public decimal OpeningCash { get; set; }
    public string? OpenNote { get; set; }
    public decimal CashSales { get; set; }
    public decimal NonCashSales { get; set; }
    public decimal CashIn { get; set; }
    public decimal CashOut { get; set; }
    public decimal CashRefunds { get; set; }
    public decimal NonCashRefunds { get; set; }
    public decimal ExpectedCash { get; set; }
    public decimal DetailExpectedCash { get; set; }
    public decimal? ActualCash { get; set; }
    public decimal? ReceivedCash { get; set; }
    public bool NeedsReconciliation { get; set; }
    public List<ReconciliationOrderDto> Orders { get; set; } = [];
    public List<ReconciliationCashDto> CashTransactions { get; set; } = [];
    public List<ReconciliationRefundDto> Refunds { get; set; } = [];
    public List<ReconciliationMovementDto> OtherMovements { get; set; } = [];
    public List<ReconciliationQrDto> Qrs { get; set; } = [];
    public List<ReconciliationAdjustmentDto> Adjustments { get; set; } = [];
    public List<ReconciliationPaymentAdjustmentDto> PaymentAdjustments { get; set; } = [];
    public List<ReconciliationDenominationDto> Denominations { get; set; } = [];
}

public sealed record ReconciliationPaymentDto(int Id, string Method, decimal Amount, string? Reference,
    string? Provider, DateTime PaidAtUtc, string Actor, bool Cancelled, bool IsDebtCollection,
    string Confirmation, bool BankVerified, bool CanRequest = false, int? PendingRequestId = null);
public sealed record ReconciliationLineDto(string Name, decimal Quantity, decimal UnitPrice, decimal Total);
public sealed class ReconciliationOrderDto
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public string Status { get; set; } = "";
    public string Customer { get; set; } = "";
    public string Actor { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public bool Cancelled { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal Deposit { get; set; }
    public decimal CashReceived { get; set; }
    public decimal BankReceived { get; set; }
    public decimal OtherReceived { get; set; }
    public decimal Remaining { get; set; }
    public decimal Surplus { get; set; }
    public decimal CashApplied { get; set; }
    public decimal NonCashApplied { get; set; }
    public bool IsCreditSale { get; set; }
    public string? Note { get; set; }
    public List<ReconciliationPaymentDto> Payments { get; set; } = [];
    public List<ReconciliationLineDto> Lines { get; set; } = [];
}
public sealed record ReconciliationCashDto(int Id, string Type, decimal Amount, string Reason, string? Note,
    DateTime CreatedAtUtc, string Actor, bool Cancelled, bool CanRequest, int? PendingRequestId, int? DepositEntryId = null);
public sealed record ReconciliationRefundDto(int Id, int OrderId, string Number, string Status, string Reason,
    string? Note, DateTime CreatedAtUtc, decimal RefundTotal, decimal DepositRestored,
    List<ReconciliationPaymentDto> Payments);
public sealed record ReconciliationMovementDto(int Id, string Kind, decimal Amount, string? Method,
    string? Reference, string? Note, int? OrderId, DateTime CreatedAtUtc, string Actor,
    int? CustomerId, string Customer, List<ReconciliationDebtAllocationDto> Allocations, bool CanRequest = false, int? PendingRequestId = null);
public sealed record ReconciliationDebtAllocationDto(int OrderId, string Number, decimal Amount);
public sealed record ReconciliationQrDto(int Id, int OrderId, decimal Amount, string Code, string Status,
    string Confirmation, string? ReviewReason, DateTime CreatedAtUtc, int? PaymentId);
public sealed record ReconciliationAdjustmentDto(int Id, int TransactionId, string Status, bool IsCancellation,
    string BeforeType, decimal BeforeAmount, string AfterType, decimal AfterAmount, decimal ExpectedDelta,
    string Reason, string Actor, DateTime CreatedAtUtc, string? Reviewer, DateTime? ReviewedAtUtc, string? ReviewNote);
public sealed record ReconciliationDenominationDto(string Type, int Value, int Quantity, decimal Amount);
public sealed record ReconciliationPaymentAdjustmentDto(int Id, int OrderId, int PaymentId, string Status,
    string OldMethod, string NewMethod, decimal Amount, string? OldReference, string? NewReference,
    decimal ExpectedDelta, string Reason, string Actor, DateTime CreatedAtUtc, string? Reviewer,
    DateTime? ReviewedAtUtc, string? ReviewNote, DateTime? ReconciledAtUtc, int? DepositEntryId = null);
