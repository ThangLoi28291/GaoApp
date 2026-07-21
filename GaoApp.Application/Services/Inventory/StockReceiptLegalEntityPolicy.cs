using GaoApp.Application.DTOs.Inventory;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Services.Inventory;

/// <summary>
/// Quy tắc chọn HKD/kho cho phiếu nhập. StockDocument không cần lưu thêm
/// LegalEntityId vì Warehouse là owner bất biến và đã được khóa FK theo Store.
/// </summary>
public static class StockReceiptLegalEntityPolicy
{
    public static StockReceiptFormOptionsDto BuildFormOptions(
        IEnumerable<LegalEntity> legalEntities)
    {
        var activeEntities = legalEntities
            .Where(x => x.IsActive && !x.IsDeleted)
            .OrderBy(x => x.SalePriority)
            .ThenBy(x => x.Code)
            .ToList();

        var preferred = activeEntities.FirstOrDefault(x => x.IsDefaultForPurchase)
            ?? activeEntities.FirstOrDefault();

        return new StockReceiptFormOptionsDto
        {
            DefaultLegalEntityId = preferred?.Id ?? 0,
            LegalEntities = activeEntities
                .Select(x => new StockReceiptLegalEntityOptionDto
                {
                    Id = x.Id,
                    Code = x.Code,
                    Name = x.Name,
                    DefaultWarehouseId = x.DefaultWarehouseId,
                    IsDefaultForPurchase = x.IsDefaultForPurchase
                })
                .ToList(),
            Warehouses = activeEntities
                .SelectMany(x => x.Warehouses
                    .Where(w => w.IsActive && !w.IsDeleted)
                    .OrderBy(w => w.Name)
                    .Select(w => new StockReceiptWarehouseOptionDto
                    {
                        Id = w.Id,
                        LegalEntityId = x.Id,
                        Code = w.Code,
                        Name = w.Name
                    }))
                .ToList()
        };
    }

    public static void EnsureWarehouseSelectable(
        int legalEntityId,
        Warehouse? warehouse)
    {
        if (legalEntityId <= 0)
            throw new InvalidOperationException("Vui lòng chọn HKD nhập hàng.");

        if (warehouse == null || warehouse.IsDeleted)
            throw new InvalidOperationException("Kho không tồn tại.");

        if (!warehouse.IsActive)
            throw new InvalidOperationException("Kho đã ngừng hoạt động.");

        if (warehouse.LegalEntityId != legalEntityId)
        {
            throw new InvalidOperationException(
                "Kho đã chọn không thuộc HKD nhập hàng. Vui lòng chọn lại kho đúng HKD.");
        }

        if (warehouse.LegalEntity == null ||
            warehouse.LegalEntity.IsDeleted ||
            !warehouse.LegalEntity.IsActive)
        {
            throw new InvalidOperationException("HKD sở hữu kho đã ngừng hoạt động.");
        }
    }
}
