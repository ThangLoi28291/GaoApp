namespace GaoApp.Domain.Delivery;

// D01 executable contract. These types are not persisted entities or client authorization DTOs.
public enum DeliveryState
{
    Created, Picking, AwaitingApproval, ReadyForHandover, HandedOver, Delivering,
    AwaitingReconciliation, ReadyToSettle, CancellationPending, Settled, Cancelled
}

public enum DeliveryCommand
{
    StartPicking, SubmitPicking, ApprovePicking, ReopenPicking, Handover, Depart,
    RecordOutcome, Reconcile, Settle, CancelBeforeHandover, RequestCancellation,
    ConfirmCancellation, ReassignCourier
}

[Flags]
public enum DeliveryCapability
{
    None = 0, View = 1, Create = 2, Pick = 4, ApproveChanges = 8, Handover = 16,
    RecordDeparture = 32, ReportOwnOutcome = 64, ReportOnBehalf = 128,
    ReceiveReturns = 256, ReconcileCash = 512, ConfirmBankTransfer = 1024,
    Finalize = 2048, Credit = 4096, Cancel = 8192, ApproveExceptions = 16384,
    ResolveShortage = 32768, ReassignCourier = 65536
}

public enum DeliveryReportSource { AtStore, Phone, WebByCourier }
public enum DeliverySettlementMethod { Cash, ConfirmedBankTransfer, CustomerCredit }

public sealed class DeliveryRuleException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

// Always resolved from authenticated server context, never deserialized from an HTTP request.
public sealed record DeliveryActor(int StoreId, int UserId, DeliveryCapability Capabilities,
    IReadOnlySet<int> WarehouseIds, IReadOnlySet<int> LegalEntityIds);

public sealed record DeliveryOrigin(int StoreId, int SourceWarehouseId, int SourceLegalEntityId,
    int CreatedByUserId, int CreatedTerminalId, int CreatedShiftId, int SourceCartId,
    DateTimeOffset CreatedAtUtc, int? CustomerId = null);

public sealed record DeliveryClosingCounter(int StoreId, int TerminalId, int ShiftId, int WarehouseId,
    int ShiftOwnerUserId, bool IsOpen);

public sealed record DeliveryWorkflowFacts(
    bool HasPickingChanges = false, bool PickingComplete = false, bool HasPickedGoods = false,
    bool CourierAssigned = false, bool CustodyHandoverConfirmed = false,
    bool GoodsReconciled = false, bool MoneyReconciled = false, bool HasDeliveredGoods = false);

public sealed record DeliveryQuantityLine(int LineId, decimal Ordered, decimal Picked,
    decimal Dispatched, decimal Delivered, decimal ReportedReturn, decimal AcceptedReturn,
    decimal Damaged, decimal Lost, decimal BaseMultiplier, decimal BaseUnitCost,
    bool ExceptionsApproved = false);

public sealed record DeliveryQuantityPlan(int LineId, decimal DispatchedBase, decimal DeliveredBase,
    decimal AcceptedReturnBase, decimal PhysicalStockDelta, decimal PendingCustody,
    decimal PendingReturnReceipt, decimal OutstandingQuantity, decimal SaleCost,
    decimal ExceptionCost, bool GoodsReconciled);

public sealed record DeliveryCostFragment(int AllocationId, int WarehouseId, int LegalEntityId,
    decimal DispatchedBase, decimal DeliveredBase, decimal AcceptedReturnBase,
    decimal DamagedBase, decimal LostBase, decimal UnitCost);

public sealed record DeliveryFragmentCostPlan(decimal SaleCost, decimal ReturnedCost, decimal ExceptionCost);

public sealed record DeliveryPriceLine(int LineId, decimal OrderedQuantity, decimal UnitPrice,
    decimal ApprovedLineDiscount = 0m);

public sealed record DeliveryApprovedPriceLine(int LineId, decimal OrderedQuantity, decimal Gross,
    decimal LineDiscount, decimal AllocatedOrderDiscount, decimal Net);

public sealed record DeliveryChargeLine(int LineId, decimal DeliveredQuantity, decimal Amount);

public sealed record DeliveryCustomerCredit(int StoreId, int CustomerId, bool IsActive, bool HaveDebt);

public sealed record DeliverySettlementRequest(DeliverySettlementMethod Method,
    decimal ReportedCustomerCash, decimal ReceivedCash, decimal ConfirmedBankAmount,
    bool BankVerified, string? BankReference, int? BankAccountStoreId,
    decimal CreditAmount, DeliveryCustomerCredit? Customer, decimal AdvanceAmount = 0m);

public sealed record DeliverySettlementPlan(decimal SaleAmount, decimal ShiftCashAmount,
    decimal NonCashAmount, decimal CustomerDebt, bool CreatesSale,
    int SettlingTerminalId, int SettlingShiftId, int SettlingCounterWarehouseId, int SourceWarehouseId, int SourceLegalEntityId,
    int CreatedTerminalId, int CreatedShiftId, DateOnly CreationDate, DateOnly RevenueDate,
    DateOnly DispatchDate, DateOnly ReconciliationDate);

public sealed record DeliveryOutcomeReport(int ActorUserId, int CourierUserId,
    DeliveryReportSource Source, DateTimeOffset RecordedAtUtc, DateTimeOffset? CustomerReceivedAtUtc);

public static class DeliveryValues
{
    public const decimal MoneyLimit = 10000000000000000m;
    public const decimal QuantityLimit = 100000000000000m;

    public static decimal RoundMoney(decimal value) => decimal.Round(value, 0, MidpointRounding.AwayFromZero);

    public static void Require(bool condition, string code, string message)
    {
        if (!condition) throw new DeliveryRuleException(code, message);
    }

    public static void Quantity(decimal value, string field)
        => Require(value >= 0m && value < QuantityLimit && decimal.Round(value, 4) == value,
            "QUANTITY_INVALID", $"{field}: số lượng phải không âm, tối đa 4 số lẻ và trong giới hạn lưu trữ.");

    public static void Money(decimal value, string field, bool wholeDong = true)
        => Require(value >= 0m && value < MoneyLimit && decimal.Round(value, wholeDong ? 0 : 2) == value,
            "MONEY_INVALID", $"{field}: số tiền không hợp lệ.");

    public static decimal PricedAmount(decimal quantity, decimal unitPrice)
    {
        Require(quantity == 0 || unitPrice < MoneyLimit / quantity,
            "MONEY_INVALID", "Thành tiền vượt giới hạn; không thực hiện phép nhân gây tràn số.");
        var amount = RoundMoney(quantity * unitPrice);
        Money(amount, "Thành tiền");
        return amount;
    }

    // Match existing valuation/allocation SQL precision: unit cost (18,6), amount (18,4).
    public static decimal CostAmount(decimal quantity, decimal unitCost)
    {
        Require(unitCost >= 0 && unitCost < 1000000000000m && decimal.Round(unitCost, 6) == unitCost,
            "COST_INVALID", "Giá vốn không khớp precision (18,6).");
        Require(quantity == 0 || unitCost < QuantityLimit / quantity,
            "COST_INVALID", "Giá trị vốn vượt precision (18,4).");
        var amount = decimal.Round(quantity * unitCost, 4, MidpointRounding.AwayFromZero);
        Require(amount < QuantityLimit, "COST_INVALID", "Giá trị vốn vượt giới hạn lưu trữ.");
        return amount;
    }

    public static void Utc(DateTimeOffset value)
        => Require(value.Offset == TimeSpan.Zero && value != default,
            "UTC_REQUIRED", "Thời điểm phải lấy từ server và lưu UTC.");

    public static DateOnly BusinessDate(DateTimeOffset utc)
    {
        Utc(utc);
        return DateOnly.FromDateTime(utc.ToOffset(TimeSpan.FromHours(7)).DateTime);
    }

    public static void Origin(DeliveryOrigin origin)
    {
        Require(origin.StoreId > 0 && origin.SourceWarehouseId > 0 && origin.SourceLegalEntityId > 0 &&
            origin.CreatedByUserId > 0 && origin.CreatedTerminalId > 0 && origin.CreatedShiftId > 0 && origin.SourceCartId > 0,
            "ORIGIN_INVALID", "Thiếu nguồn cửa hàng/kho/chủ thể/quầy/ca/giỏ tạo đơn.");
        Require(origin.CustomerId is null or > 0, "ORIGIN_INVALID", "Khách nguồn không hợp lệ.");
        Utc(origin.CreatedAtUtc);
    }
}
