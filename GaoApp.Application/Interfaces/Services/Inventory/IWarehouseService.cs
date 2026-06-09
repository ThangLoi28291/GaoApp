using GaoApp.Application.DTOs.Common;
using GaoApp.Application.DTOs.Inventory.Warehouse;


namespace GaoApp.Application.Interfaces.Services.Inventory;

public interface IWarehouseService
{
    Task<WarehouseDto> CreateAsync(CreateWarehouseRequest request, CancellationToken ct = default);
    Task<WarehouseDto> UpdateAsync(UpdateWarehouseRequest request, CancellationToken ct = default);
    Task<List<WarehouseDto>> GetAllAsync(CancellationToken ct = default);
    

Task<List<Select2OptionDto>> SearchSelect2Async(string? term, CancellationToken ct = default);
    Task<WarehouseDto> ToggleAllowNegativeInventoryAsync(int id, bool allow, CancellationToken ct = default);
    Task<WarehouseDto> ToggleActiveAsync(int id, bool isActive, CancellationToken ct = default);
    Task<WarehouseDto> SetDefaultAsync(int id, CancellationToken ct = default);
}