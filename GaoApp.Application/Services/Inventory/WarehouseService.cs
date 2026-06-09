using GaoApp.Application.DTOs.Common;
using GaoApp.Application.DTOs.Inventory.Warehouse;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Services.Inventory;

public class WarehouseService : IWarehouseService
{
    private readonly IWarehouseRepository _warehouseRepository;

    public WarehouseService(IWarehouseRepository warehouseRepository)
    {
        _warehouseRepository = warehouseRepository;
    }

    public async Task<WarehouseDto> CreateAsync(CreateWarehouseRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new InvalidOperationException("Tên kho không được để trống.");

        var code = await _warehouseRepository.GenerateNextCodeAsync(ct);
        var name = request.Name.Trim();

        if (await _warehouseRepository.ExistsNameAsync(name, null, ct))
            throw new InvalidOperationException("Tên kho đã tồn tại.");

        if (request.IsDefault)
        {
            await _warehouseRepository.ClearDefaultAsync(null, ct);
        }

        var warehouse = new Warehouse
        {
            Code = code,
            Name = name,
            Location = request.Location?.Trim(),
            Note = request.Note?.Trim(),
            IsDefault = request.IsDefault,
            IsActive = true,
            AllowNegativeInventory = request.AllowNegativeInventory
        };

        await _warehouseRepository.AddAsync(warehouse, ct);
        await _warehouseRepository.SaveChangesAsync(ct);

        return MapToDto(warehouse);
    }

    public async Task<WarehouseDto> UpdateAsync(UpdateWarehouseRequest request, CancellationToken ct = default)
    {
        var warehouse = await _warehouseRepository.GetByIdAsync(request.Id, ct)
            ?? throw new InvalidOperationException("Không tìm thấy kho.");

        if (string.IsNullOrWhiteSpace(request.Code))
            throw new InvalidOperationException("Mã kho không được để trống.");

        if (string.IsNullOrWhiteSpace(request.Name))
            throw new InvalidOperationException("Tên kho không được để trống.");

        var code = request.Code.Trim();
        var name = request.Name.Trim();

        if (await _warehouseRepository.ExistsCodeAsync(code, request.Id, ct))
            throw new InvalidOperationException("Mã kho đã tồn tại.");

        if (await _warehouseRepository.ExistsNameAsync(name, request.Id, ct))
            throw new InvalidOperationException("Tên kho đã tồn tại.");

        // Nếu kho đang sửa được chọn làm mặc định
        // thì phải bỏ mặc định của các kho khác trước
        if (request.IsDefault)
        {
            await _warehouseRepository.ClearDefaultAsync(request.Id, ct);
        }

        warehouse.Code = code;
        warehouse.Name = name;
        warehouse.Location = request.Location?.Trim();
        warehouse.Note = request.Note?.Trim();
        warehouse.IsDefault = request.IsDefault;
        warehouse.IsActive = request.IsActive;
        warehouse.AllowNegativeInventory = request.AllowNegativeInventory;

        await _warehouseRepository.SaveChangesAsync(ct);

        return MapToDto(warehouse);
    }


    public async Task<List<WarehouseDto>> GetAllAsync(CancellationToken ct = default)
    {
        var items = await _warehouseRepository.GetAllAsync(ct);

        return items.Select(MapToDto).ToList();
    }
    public async Task<List<Select2OptionDto>> SearchSelect2Async(string? term, CancellationToken ct = default)
    {
        var items = await _warehouseRepository.GetAllAsync(ct);

        if (!string.IsNullOrWhiteSpace(term))
        {
            term = term.Trim();
            items = items
                .Where(x => !string.IsNullOrWhiteSpace(x.Name) &&
                            x.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return items
            .OrderBy(x => x.Name)
            .Take(20)
            .Select(x => new Select2OptionDto
            {
                Id = x.Id.ToString(),
                Text = x.Name,
                IsDefault = x.IsDefault
            })
            .ToList();
    }
    private static WarehouseDto MapToDto(Warehouse x)
    {
        return new WarehouseDto
        {
            Id = x.Id,
            Code = x.Code,
            Name = x.Name,
            Location = x.Location,
            Note = x.Note,
            IsDefault = x.IsDefault,
            IsActive = x.IsActive,
            AllowNegativeInventory = x.AllowNegativeInventory
        };
    }
    public async Task<WarehouseDto> ToggleAllowNegativeInventoryAsync(int id, bool allow, CancellationToken ct = default)
    {
        var warehouse = await _warehouseRepository.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException("Không tìm thấy kho.");

        warehouse.AllowNegativeInventory = allow;

        await _warehouseRepository.SaveChangesAsync(ct);

        return MapToDto(warehouse);
    }

    public async Task<WarehouseDto> ToggleActiveAsync(int id, bool isActive, CancellationToken ct = default)
    {
        var warehouse = await _warehouseRepository.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException("Không tìm thấy kho.");

        if (warehouse.IsDefault && !isActive)
            throw new InvalidOperationException("Không thể ngưng sử dụng kho mặc định.");

        warehouse.IsActive = isActive;

        await _warehouseRepository.SaveChangesAsync(ct);

        return MapToDto(warehouse);
    }

    public async Task<WarehouseDto> SetDefaultAsync(int id, CancellationToken ct = default)
    {
        var warehouse = await _warehouseRepository.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException("Không tìm thấy kho.");

        if (!warehouse.IsActive)
            throw new InvalidOperationException("Không thể đặt kho ngưng sử dụng làm kho mặc định.");

        await _warehouseRepository.ClearDefaultAsync(id, ct);

        warehouse.IsDefault = true;

        await _warehouseRepository.SaveChangesAsync(ct);

        return MapToDto(warehouse);
    }
}