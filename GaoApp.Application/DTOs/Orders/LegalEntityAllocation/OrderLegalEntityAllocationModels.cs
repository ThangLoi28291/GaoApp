using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Orders.LegalEntityAllocation;

/// <summary>
/// Snapshot đầy đủ để chạy allocation thuần. Caller chịu trách nhiệm lấy dữ liệu
/// đúng Store; engine kiểm tra StoreId và tuyệt đối không đọc/ghi database.
/// </summary>
public sealed class OrderLegalEntityAllocationRequest
{
    public int StoreId { get; init; }
    public int OrderId { get; init; }

    /// <summary>
    /// Finalize POS được phép ghi phần thiếu vào kho của HKD có SalePriority cuối cùng.
    /// Preview/hold mặc định vẫn false để chỉ phản ánh tồn khả dụng thực tế.
    /// </summary>
    public bool AllowNegativeInventory { get; init; }

    public IReadOnlyCollection<OrderLegalEntityAllocationLineInput> Lines { get; init; }
        = Array.Empty<OrderLegalEntityAllocationLineInput>();
    public IReadOnlyCollection<LegalEntityAllocationSourceInput> EligibleSources { get; init; }
        = Array.Empty<LegalEntityAllocationSourceInput>();
    public IReadOnlyCollection<LegalEntityInventoryAvailabilityInput> Inventory { get; init; }
        = Array.Empty<LegalEntityInventoryAvailabilityInput>();
}

public sealed class OrderLegalEntityAllocationLineInput
{
    public int OrderLineId { get; init; }
    public int ProductVariantId { get; init; }
    public int? ProductUnitConversionId { get; init; }
    public decimal Quantity { get; init; }
    public decimal Multiplier { get; init; } = 1m;
    public decimal BaseQuantity { get; init; }
}

/// <summary>
/// Mỗi LegalEntity chỉ đưa vào một kho bán mặc định ở Phase 22.4.
/// </summary>
public sealed class LegalEntityAllocationSourceInput
{
    public int LegalEntityId { get; init; }
    public int WarehouseId { get; init; }
    public int SalePriority { get; init; }
}

public sealed class LegalEntityInventoryAvailabilityInput
{
    public int WarehouseId { get; init; }
    public int ProductVariantId { get; init; }
    public decimal OnHandBaseQuantity { get; init; }
    public decimal ReservedBaseQuantity { get; init; }
    public decimal AvailableBaseQuantity =>
        Math.Max(0m, OnHandBaseQuantity - ReservedBaseQuantity);
}

public sealed class OrderLegalEntityAllocationResult
{
    public int StoreId { get; init; }
    public int OrderId { get; init; }
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty;
    public decimal TotalRequiredBaseQuantity { get; init; }
    public decimal TotalAllocatedBaseQuantity { get; init; }
    public decimal TotalShortageBaseQuantity { get; init; }
    public int LegalEntityCount { get; init; }
    public bool HasMultipleLegalEntities => LegalEntityCount > 1;
    public IReadOnlyList<OrderLegalEntityAllocationItem> Allocations { get; init; }
        = Array.Empty<OrderLegalEntityAllocationItem>();
    public IReadOnlyList<OrderLegalEntityAllocationShortage> Shortages { get; init; }
        = Array.Empty<OrderLegalEntityAllocationShortage>();
}

public sealed class OrderLegalEntityAllocationItem
{
    public int StoreId { get; init; }
    public int OrderId { get; init; }
    public int OrderLineId { get; init; }
    public int ProductVariantId { get; init; }
    public int? ProductUnitConversionId { get; init; }
    public int LegalEntityId { get; init; }
    public int WarehouseId { get; init; }
    public int SalePriority { get; init; }
    public decimal Quantity { get; init; }
    public decimal BaseQuantity { get; init; }
    public OrderLegalEntityAllocationSource AllocationSource { get; init; }
}

public sealed class OrderLegalEntityAllocationShortage
{
    public int OrderLineId { get; init; }
    public int ProductVariantId { get; init; }
    public decimal RequiredBaseQuantity { get; init; }
    public decimal AllocatedBaseQuantity { get; init; }
    public decimal ShortageBaseQuantity { get; init; }
    public string Message { get; init; } = string.Empty;
}
