using GaoApp.Application.DTOs.Orders.LegalEntityAllocation;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Orders;

/// <summary>
/// Allocation engine thuần của Phase 22.4.
/// Mọi phép trừ chỉ diễn ra trên dictionary cục bộ để nhiều dòng trong cùng đơn
/// không dùng trùng một lượng tồn. Không có bất kỳ side effect nào ra database.
/// </summary>
public sealed class OrderLegalEntityAllocationService : IOrderLegalEntityAllocationService
{
    private const decimal QuantityTolerance = 0.0001m;

    public OrderLegalEntityAllocationResult Preview(
        OrderLegalEntityAllocationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var lines = ValidateAndGetLines(request);
        var sources = ValidateAndGetSources(request);
        var inventory = ValidateAndGetInventory(request);

        var remainingByWarehouseAndVariant = inventory.ToDictionary(
            x => (x.WarehouseId, x.ProductVariantId),
            x => x.AvailableBaseQuantity);

        var allocations = new List<OrderLegalEntityAllocationItem>();
        var shortages = new List<OrderLegalEntityAllocationShortage>();

        foreach (var line in lines)
        {
            var remainingRequired = line.BaseQuantity;

            foreach (var source in sources)
            {
                if (remainingRequired <= 0m)
                    break;

                var key = (source.WarehouseId, line.ProductVariantId);
                var available = remainingByWarehouseAndVariant.GetValueOrDefault(key);
                if (available <= 0m)
                    continue;

                var allocatedBaseQuantity = Math.Min(available, remainingRequired);
                allocations.Add(new OrderLegalEntityAllocationItem
                {
                    StoreId = request.StoreId,
                    OrderId = request.OrderId,
                    OrderLineId = line.OrderLineId,
                    ProductVariantId = line.ProductVariantId,
                    ProductUnitConversionId = line.ProductUnitConversionId,
                    LegalEntityId = source.LegalEntityId,
                    WarehouseId = source.WarehouseId,
                    SalePriority = source.SalePriority,
                    Quantity = allocatedBaseQuantity / line.Multiplier,
                    BaseQuantity = allocatedBaseQuantity,
                    AllocationSource = OrderLegalEntityAllocationSource.AutoBySalePriority
                });

                remainingByWarehouseAndVariant[key] = available - allocatedBaseQuantity;
                remainingRequired -= allocatedBaseQuantity;
            }

            if (remainingRequired > 0m)
            {
                var allocated = line.BaseQuantity - remainingRequired;
                shortages.Add(new OrderLegalEntityAllocationShortage
                {
                    OrderLineId = line.OrderLineId,
                    ProductVariantId = line.ProductVariantId,
                    RequiredBaseQuantity = line.BaseQuantity,
                    AllocatedBaseQuantity = allocated,
                    ShortageBaseQuantity = remainingRequired,
                    Message = $"Dòng #{line.OrderLineId} thiếu {remainingRequired:n4} " +
                              $"đơn vị gốc của sản phẩm #{line.ProductVariantId}."
                });

                if (request.AllowNegativeInventory)
                {
                    var fallback = sources[^1];
                    var existingIndex = allocations.FindIndex(x =>
                        x.OrderLineId == line.OrderLineId &&
                        x.LegalEntityId == fallback.LegalEntityId &&
                        x.WarehouseId == fallback.WarehouseId);

                    if (existingIndex >= 0)
                    {
                        var existing = allocations[existingIndex];
                        var combinedBaseQuantity = existing.BaseQuantity + remainingRequired;
                        allocations[existingIndex] = new OrderLegalEntityAllocationItem
                        {
                            StoreId = existing.StoreId,
                            OrderId = existing.OrderId,
                            OrderLineId = existing.OrderLineId,
                            ProductVariantId = existing.ProductVariantId,
                            ProductUnitConversionId = existing.ProductUnitConversionId,
                            LegalEntityId = existing.LegalEntityId,
                            WarehouseId = existing.WarehouseId,
                            SalePriority = existing.SalePriority,
                            Quantity = combinedBaseQuantity / line.Multiplier,
                            BaseQuantity = combinedBaseQuantity,
                            AllocationSource = OrderLegalEntityAllocationSource.AutoNegativeFallback
                        };
                    }
                    else
                    {
                        allocations.Add(new OrderLegalEntityAllocationItem
                        {
                            StoreId = request.StoreId,
                            OrderId = request.OrderId,
                            OrderLineId = line.OrderLineId,
                            ProductVariantId = line.ProductVariantId,
                            ProductUnitConversionId = line.ProductUnitConversionId,
                            LegalEntityId = fallback.LegalEntityId,
                            WarehouseId = fallback.WarehouseId,
                            SalePriority = fallback.SalePriority,
                            Quantity = remainingRequired / line.Multiplier,
                            BaseQuantity = remainingRequired,
                            AllocationSource = OrderLegalEntityAllocationSource.AutoNegativeFallback
                        });
                    }
                }
            }
        }

        var requiredTotal = lines.Sum(x => x.BaseQuantity);
        var allocatedTotal = allocations.Sum(x => x.BaseQuantity);
        var shortageTotal = shortages.Sum(x => x.ShortageBaseQuantity);
        var isSuccess = shortages.Count == 0 || request.AllowNegativeInventory;

        return new OrderLegalEntityAllocationResult
        {
            StoreId = request.StoreId,
            OrderId = request.OrderId,
            IsSuccess = isSuccess,
            Message = isSuccess
                ? shortages.Count == 0
                    ? "Đủ tồn để phân bổ theo thứ tự ưu tiên HKD."
                    : $"Đã phân bổ đủ đơn; {shortageTotal:n4} đơn vị gốc được ghi âm vào HKD cuối."
                : $"Không đủ tồn; còn thiếu {shortageTotal:n4} đơn vị gốc.",
            TotalRequiredBaseQuantity = requiredTotal,
            TotalAllocatedBaseQuantity = allocatedTotal,
            TotalShortageBaseQuantity = shortageTotal,
            LegalEntityCount = allocations
                .Select(x => x.LegalEntityId)
                .Distinct()
                .Count(),
            Allocations = allocations,
            Shortages = shortages
        };
    }

    private static List<OrderLegalEntityAllocationLineInput> ValidateAndGetLines(
        OrderLegalEntityAllocationRequest request)
    {
        if (request.StoreId <= 0)
            throw new ArgumentException("StoreId phải lớn hơn 0.", nameof(request));

        if (request.OrderId <= 0)
            throw new ArgumentException("OrderId phải lớn hơn 0.", nameof(request));

        var lines = request.Lines?.ToList()
            ?? throw new ArgumentException("Danh sách dòng đơn không được null.", nameof(request));

        if (lines.Count == 0)
            throw new ArgumentException("Đơn phải có ít nhất một dòng để allocation.", nameof(request));

        if (lines.GroupBy(x => x.OrderLineId).Any(x => x.Count() > 1))
            throw new ArgumentException("OrderLineId không được trùng trong request allocation.", nameof(request));

        foreach (var line in lines)
        {
            if (line.OrderLineId <= 0 || line.ProductVariantId <= 0)
                throw new ArgumentException("OrderLineId và ProductVariantId phải lớn hơn 0.", nameof(request));

            if (line.Quantity <= 0 || line.Multiplier <= 0 || line.BaseQuantity <= 0)
                throw new ArgumentException("Quantity, Multiplier và BaseQuantity phải lớn hơn 0.", nameof(request));

            var expectedBaseQuantity = line.Quantity * line.Multiplier;
            if (Math.Abs(expectedBaseQuantity - line.BaseQuantity) > QuantityTolerance)
            {
                throw new ArgumentException(
                    $"Dòng #{line.OrderLineId} có BaseQuantity không bằng Quantity × Multiplier.",
                    nameof(request));
            }
        }

        return lines
            .OrderBy(x => x.OrderLineId)
            .ToList();
    }

    private static List<LegalEntityAllocationSourceInput> ValidateAndGetSources(
        OrderLegalEntityAllocationRequest request)
    {
        var sources = request.EligibleSources?.ToList()
            ?? throw new ArgumentException("Danh sách nguồn HKD không được null.", nameof(request));

        if (sources.Count == 0)
            throw new ArgumentException("Phải có ít nhất một HKD active để allocation.", nameof(request));

        foreach (var source in sources)
        {
            if (source.LegalEntityId <= 0 || source.WarehouseId <= 0 || source.SalePriority <= 0)
            {
                throw new ArgumentException(
                    "LegalEntityId, WarehouseId và SalePriority phải lớn hơn 0.",
                    nameof(request));
            }
        }

        if (sources.GroupBy(x => x.LegalEntityId).Any(x => x.Count() > 1))
            throw new ArgumentException("Mỗi HKD chỉ được có một kho nguồn trong allocation.", nameof(request));

        if (sources.GroupBy(x => x.WarehouseId).Any(x => x.Count() > 1))
            throw new ArgumentException("Một kho không được gán cho nhiều HKD trong allocation.", nameof(request));

        if (sources.GroupBy(x => x.SalePriority).Any(x => x.Count() > 1))
            throw new ArgumentException("SalePriority của các HKD không được trùng.", nameof(request));

        return sources
            .OrderBy(x => x.SalePriority)
            .ThenBy(x => x.LegalEntityId)
            .ToList();
    }

    private static List<LegalEntityInventoryAvailabilityInput> ValidateAndGetInventory(
        OrderLegalEntityAllocationRequest request)
    {
        var inventory = request.Inventory?.ToList()
            ?? throw new ArgumentException("Snapshot tồn kho không được null.", nameof(request));

        foreach (var item in inventory)
        {
            if (item.WarehouseId <= 0 || item.ProductVariantId <= 0)
                throw new ArgumentException("WarehouseId và ProductVariantId phải lớn hơn 0.", nameof(request));

            if (item.ReservedBaseQuantity < 0)
                throw new ArgumentException("ReservedBaseQuantity không được âm.", nameof(request));
        }

        if (inventory
            .GroupBy(x => (x.WarehouseId, x.ProductVariantId))
            .Any(x => x.Count() > 1))
        {
            throw new ArgumentException(
                "Snapshot tồn kho bị trùng WarehouseId và ProductVariantId.",
                nameof(request));
        }

        return inventory;
    }
}
