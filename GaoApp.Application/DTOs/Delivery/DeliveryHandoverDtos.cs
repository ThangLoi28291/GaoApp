using System.Text.Json.Serialization;

namespace GaoApp.Application.DTOs.Delivery;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliveryHandoverLineRequest(int LineId, string DeliveredBaseQuantityText,
    string ActualReturnedBaseQuantityText);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliveryHandoverPreviewRequest(string ExpectedTripVersion, string ExpectedOrderVersion,
    IReadOnlyList<DeliveryHandoverLineRequest> Lines);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliveryHandoverRequest(Guid ClientRequestId, string ExpectedTripVersion,
    string ExpectedOrderVersion, int ExpectedTerminalId, int ExpectedShiftId, int CourierUserId,
    bool PhysicalGoodsConfirmed, string Outcome, string? Reason, DateTimeOffset? CustomerReceivedAtUtc,
    IReadOnlyList<DeliveryHandoverLineRequest> Lines, string PaymentKind, string ReportedCashText,
    string ActualReceivedCashText, string ConfirmedTransferText, bool TransferConfirmed,
    int? BankAccountId = null, string? TransferReference = null, DateOnly? CreditDueDate = null,
    string? CreditNote = null);

public sealed record DeliveryHandoverCounterDto(int StoreId, int TerminalId, int ShiftId,
    int WarehouseId, int OwnerUserId, bool IsOpen)
{
    public string? TerminalName { get; init; }
}
public sealed record DeliveryHandoverCapabilitiesDto(bool CanFinalize, bool CanReceiveReturns,
    bool CanReconcileCash, bool CanConfirmBankTransfer, bool CanCredit);
public sealed record DeliveryHandoverBankDto(int BankAccountId, string Name);
public sealed record DeliveryHandoverLinePreviewDto(int LineId, string DeliveredBaseQuantityText,
    string ActualReturnedBaseQuantityText, string NewReturnBaseQuantityText,
    string GrossText, string DiscountText, string NetText);
public sealed record DeliveryHandoverPreviewDto(int TripId, int DeliveryOrderId, string TripVersion,
    string OrderVersion, int SourceWarehouseId, int SourceLegalEntityId, string SourceHash,
    string PayableText, IReadOnlyList<DeliveryHandoverLinePreviewDto> Lines);
public sealed record DeliveryHandoverResultDto(int DeliveryOrderId, int? SaleOrderId, string State,
    string PaymentKind, string PayableText, string ReceivedCashText, string ConfirmedTransferText,
    string CustomerDebtText, int TerminalId, int ShiftId, int SourceWarehouseId, int SourceLegalEntityId,
    DateTimeOffset RecordedAtUtc);
public sealed record DeliveryHandoverOrderDto(int DeliveryOrderId, int? SourceCustomerId,
    bool CustomerCreditEligible, bool CanConfirm, DeliveryHandoverPreviewDto? SavedPreview,
    DeliveryHandoverResultDto? Settlement, string? CostQuality)
{
    public IReadOnlyList<DeliveryHandoverLineRequest>? SettledLines { get; init; }
}
public sealed record DeliveryHandoverWorkspaceDto(DeliveryTripDetailDto Trip,
    DeliveryHandoverCounterDto? Counter, DeliveryHandoverCapabilitiesDto Capabilities,
    IReadOnlyList<DeliveryHandoverBankDto> BankAccounts, IReadOnlyList<DeliveryHandoverOrderDto> Orders);
public sealed record DeliveryHandoverAck(Guid ClientRequestId, int TripId, int DeliveryOrderId,
    string Operation, int AppliedTripRevision, string AppliedTripVersion, int AppliedOrderRevision,
    string AppliedOrderVersion, string State, DateTimeOffset RecordedAtUtc, bool Replayed,
    DeliveryHandoverResultDto Settlement);

// Server-only proof contract. HTTP endpoints never accept sale prices, source identities or costs.
public sealed record DeliverySaleCostSlice(int DeliveryLineId, int DispatchFragmentId,
    int InventoryTransactionId, int ValuationEntryId, int BudgetDebitId, int LayerId,
    string StartBaseQuantityText, string EndBaseQuantityText, string CostAmountText,
    string Quality);
public sealed record DeliverySaleLinePlan(int DeliveryLineId, int SourceOrderLineId, int VariantId,
    decimal DeliveredBaseQuantity, decimal Gross, decimal Discount, decimal Net,
    decimal RecordedCost, string CostQuality, IReadOnlyList<DeliverySaleCostSlice> Slices)
{
    public string ItemName { get; init; } = "";
    public int? SellingUnitId { get; init; }
    public int? BaseUnitId { get; init; }
    public int? ProductUnitConversionId { get; init; }
    public string UnitName { get; init; } = "";
    public string BaseUnitName { get; init; } = "";
    public decimal SourceMultiplier { get; init; }
    public decimal SourceUnitPrice { get; init; }
    public decimal SourceOrderedQuantity { get; init; }
    public decimal SourceLineDiscount { get; init; }
    public int ProductId { get; init; }
    public decimal ApprovedGross { get; init; }
    public decimal ApprovedNet { get; init; }
}
public sealed record DeliverySaleFinalizePlan(int StoreId, int ActorUserId, int SourceOrderId,
    int DeliveryOrderId, int SourceWarehouseId, int SourceLegalEntityId, int TerminalId, int ShiftId,
    Guid ClientRequestId, string PaymentKind, decimal Amount, decimal Cash, decimal Transfer,
    decimal Credit, int? BankAccountId, string? BankReference, DateOnly? CreditDueDate,
    string? CreditNote, DateTimeOffset RecordedAtUtc, IReadOnlyList<DeliverySaleLinePlan> Lines)
{
    public int? SourceCustomerId { get; init; }
}
public sealed record DeliverySaleFinalizeResult(int SaleOrderId);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliveryHandoverSnapshot(string Schema, int StoreId, int TripId,
    int DeliveryOrderId, int TripOrderId, int CustodyEventId, int SourceOrderId,
    int SourceWarehouseId, int SourceLegalEntityId, int OrderRevision, string OrderVersion,
    Guid ClientRequestId, int ActorUserId, int CourierUserId, string ReportSource, string Outcome,
    string? Reason, DateTimeOffset? CustomerReceivedAtUtc, DeliveryHandoverResultDto Settlement,
    IReadOnlyList<DeliveryHandoverLineRequest> Lines, IReadOnlyList<DeliverySaleLinePlan> SaleLines)
{
    public int TripRevision { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliveryHandoverCommandEnvelope(int OrderId, DeliveryHandoverRequest Request);
