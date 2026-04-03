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
        if (string.IsNullOrWhiteSpace(request.Code))
            throw new InvalidOperationException("Mã kho không được để trống.");

        if (string.IsNullOrWhiteSpace(request.Name))
            throw new InvalidOperationException("Tên kho không được để trống.");

        var code = request.Code.Trim();
        var name = request.Name.Trim();

        if (await _warehouseRepository.ExistsCodeAsync(code, null, ct))
            throw new InvalidOperationException("Mã kho đã tồn tại.");

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
            IsActive = true
        };

        await _warehouseRepository.AddAsync(warehouse, ct);
        await _warehouseRepository.SaveChangesAsync(ct);

        return new WarehouseDto
        {
            Id = warehouse.Id,
            Code = warehouse.Code,
            Name = warehouse.Name,
            Location = warehouse.Location,
            Note = warehouse.Note,
            IsDefault = warehouse.IsDefault,
            IsActive = warehouse.IsActive
        };
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

        await _warehouseRepository.SaveChangesAsync(ct);

        return new WarehouseDto
        {
            Id = warehouse.Id,
            Code = warehouse.Code,
            Name = warehouse.Name,
            Location = warehouse.Location,
            Note = warehouse.Note,
            IsDefault = warehouse.IsDefault,
            IsActive = warehouse.IsActive
        };
    }

    public async Task<List<WarehouseDto>> GetAllAsync(CancellationToken ct = default)
    {
        var items = await _warehouseRepository.GetAllAsync(ct);

        return items.Select(x => new WarehouseDto
        {
            Id = x.Id,
            Code = x.Code,
            Name = x.Name,
            Location = x.Location,
            Note = x.Note,
            IsDefault = x.IsDefault,
            IsActive = x.IsActive
        }).ToList();
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
                Text = x.Name
            })
            .ToList();
    }
}