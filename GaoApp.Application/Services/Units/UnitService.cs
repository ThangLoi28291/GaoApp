using AutoMapper;
using FluentValidation;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Units;
using GaoApp.Application.Features.Units;
using GaoApp.Application.Interfaces.Repositories.Units;
using GaoApp.Application.Interfaces.Services.Units;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Services.Units;

public sealed class UnitService : IUnitService
{
    private readonly IUnitRepository _repo;
    private readonly IMapper _mapper;
    private readonly IValidator<UnitEditDto> _validator;

    public UnitService(
        IUnitRepository repo,
        IMapper mapper,
        IValidator<UnitEditDto> validator)
    {
        _repo = repo;
        _mapper = mapper;
        _validator = validator;
    }

    public async Task<PagedResult<UnitListItemDto>> GetPagedAsync(
        int storeId, string? search, int page, int pageSize, CancellationToken ct = default)
    {
        var (items, total) = await _repo.GetPagedAsync(storeId, search, page, pageSize, ct);

        return new PagedResult<UnitListItemDto>
        {
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            Items = _mapper.Map<List<UnitListItemDto>>(items)
        };
    }

    public async Task<Result<UnitEditDto>> GetForEditAsync(int storeId, int id, CancellationToken ct = default)
    {
        if (id <= 0)
            return Result.Failure<UnitEditDto>(UnitErrors.InvalidId);

        var entity = await _repo.GetByIdAsync(storeId, id, ct);
        if (entity == null)
            return Result.Failure<UnitEditDto>(UnitErrors.NotFound);

        var dto = _mapper.Map<UnitEditDto>(entity);
        return Result.Success(dto);
    }

    public async Task<Result<int>> CreateAsync(int storeId, CreateUnitRequest dto, int? userId, CancellationToken ct = default)
    {
        if (dto == null)
            return Result.Failure<int>(UnitErrors.InvalidInput);

        var editDto = _mapper.Map<UnitEditDto>(dto);
        Normalize(editDto);

        // Create cho phép bỏ trống code => tự sinh
        if (string.IsNullOrWhiteSpace(editDto.Code))
            editDto.Code = await GenerateUnitCodeAsync(storeId, ct);

        var validationResult = await _validator.ValidateAsync(editDto, ct);
        if (!validationResult.IsValid)
            return validationResult.ToFailureResult<int>();

        if (await _repo.ExistsCodeAsync(storeId, editDto.Code, null, ct))
            return Result.Failure<int>(UnitErrors.DuplicateCode);

        if (await _repo.ExistsNameAsync(storeId, editDto.Name, null, ct))
            return Result.Failure<int>(UnitErrors.DuplicateName);

        var entity = _mapper.Map<Unit>(editDto);
        entity.StoreId = storeId;

        try
        {
            await _repo.AddAsync(entity, ct);
            await _repo.SaveChangesAsync(ct);

            if (entity.Id <= 0)
                return Result.Failure<int>(UnitErrors.CreateFailed);

            return Result.Success(entity.Id);
        }
        catch
        {
            return Result.Failure<int>(UnitErrors.CreateFailed);
        }
    }

    public async Task<Result> UpdateAsync(int storeId, UpdateUnitRequest dto, int? userId, CancellationToken ct = default)
    {
        if (dto == null || dto.Id <= 0)
            return Result.Failure(UnitErrors.InvalidId);

        var entity = await _repo.GetByIdAsync(storeId, dto.Id, ct);
        if (entity == null)
            return Result.Failure(UnitErrors.NotFound);

        var editDto = _mapper.Map<UnitEditDto>(dto);
        Normalize(editDto);

        var validationResult = await _validator.ValidateAsync(editDto, ct);
        if (!validationResult.IsValid)
            return validationResult.ToFailureResult();

        if (await _repo.ExistsCodeAsync(storeId, editDto.Code, dto.Id, ct))
            return Result.Failure(UnitErrors.DuplicateCode);

        if (await _repo.ExistsNameAsync(storeId, editDto.Name, dto.Id, ct))
            return Result.Failure(UnitErrors.DuplicateName);

        // Gán RowVersion để EF check concurrency
        entity.RowVersion = dto.RowVersion;

        // Update an toàn, chỉ gán field cho phép sửa
        entity.Code = editDto.Code;
        entity.Name = editDto.Name;
        entity.IsActive = editDto.Status;
        entity.IsBase = editDto.IsBase;
        entity.SortOrder = editDto.SortOrder;

        try
        {
            await _repo.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (Exception ex) when (ex.GetType().Name == "DbUpdateConcurrencyException")
        {
            return Result.Failure(UnitErrors.ConcurrencyConflict);
        }
        catch
        {
            return Result.Failure(UnitErrors.UpdateFailed);
        }
    }

    public async Task<Result<bool>> ToggleStatusAsync(int storeId, int id, int? userId, CancellationToken ct = default)
    {
        if (id <= 0)
            return Result.Failure<bool>(UnitErrors.InvalidId);

        var entity = await _repo.GetByIdAsync(storeId, id, ct);
        if (entity == null)
            return Result.Failure<bool>(UnitErrors.NotFound);

        entity.IsActive = !entity.IsActive;

        try
        {
            await _repo.SaveChangesAsync(ct);
            return Result.Success(entity.IsActive);
        }
        catch
        {
            return Result.Failure<bool>(UnitErrors.ToggleStatusFailed);
        }
    }

    public async Task<Result> SoftDeleteAsync(int storeId, int id, int? userId, CancellationToken ct = default)
    {
        if (id <= 0)
            return Result.Failure(UnitErrors.InvalidId);

        var entity = await _repo.GetByIdAsync(storeId, id, ct);
        if (entity == null)
            return Result.Failure(UnitErrors.NotFound);

        try
        {
            _repo.Remove(entity);
            await _repo.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch
        {
            return Result.Failure(UnitErrors.DeleteFailed);
        }
    }

    private static void Normalize(UnitEditDto dto)
    {
        dto.Code = (dto.Code ?? string.Empty).Trim().ToUpperInvariant();
        dto.Name = (dto.Name ?? string.Empty).Trim();
    }

    private async Task<string> GenerateUnitCodeAsync(int storeId, CancellationToken ct)
    {
        for (var i = 0; i < 50; i++)
        {
            var code = "DVT" + Random.Shared.Next(100000, 999999);
            if (!await _repo.ExistsCodeAsync(storeId, code, null, ct))
                return code;
        }

        return "DVT" + DateTime.UtcNow.ToString("yyMMddHHmmss");
    }
}