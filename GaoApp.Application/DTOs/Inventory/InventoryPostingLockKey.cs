namespace GaoApp.Application.DTOs.Inventory;

public sealed record InventoryPostingLockKey(
    int StoreId,
    int WarehouseId,
    int ProductVariantId);
