using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

/// <summary>Actual movement of money. Corrections append a reversal; recognition is kept separate.</summary>
public sealed class TreasuryEntry : BaseStoreEntity, IAuditTrackedEntity
{
    public Guid ClientRequestId { get; set; }
    public string RequestJson { get; set; } = "";
    public string Fund { get; set; } = "cash";
    public string? TargetFund { get; set; }
    public decimal Amount { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string Name { get; set; } = "";
    public string? Reference { get; set; }
    public string? Note { get; set; }
    public int? OperatingExpenseId { get; set; }
    public int? PurchasePayableId { get; set; }
    public int? POSShiftCashTransactionId { get; set; }
    public bool IsVoucherLink { get; set; }
    public int? ReversalOfId { get; set; }
    public DateTime? ReversedAtUtc { get; set; }
}

/// <summary>Verified balance at the beginning of a local day. One immutable baseline per fund.</summary>
public sealed class TreasuryOpeningBalance : BaseStoreEntity, IAuditTrackedEntity
{
    public string Fund { get; set; } = "cash";
    public DateTime AsOfDate { get; set; }
    public decimal Amount { get; set; }
    public string Note { get; set; } = "";
}
