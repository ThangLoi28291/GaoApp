using FluentValidation;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.AttributeValues;
using GaoApp.Application.Features.AttributeValues;
using GaoApp.Application.Interfaces.Repositories.AttributeValues;
using GaoApp.Application.Interfaces.Repositories.ProductAttributes;
using GaoApp.Application.Interfaces.Services.AttributeValues;
using GaoApp.Application.Mappings.AttributeValues;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Services.AttributeValues;

public sealed class AttributeValueService : IAttributeValueService
{
    private readonly IAttributeValueRepository _repo;
    private readonly IProductAttributeRepository _attrRepo;
    private readonly IValidator<AttributeValueEditDto> _validator;

    public AttributeValueService(
        IAttributeValueRepository repo,
        IProductAttributeRepository attrRepo,
        IValidator<AttributeValueEditDto> validator)
    {
        _repo = repo;
        _attrRepo = attrRepo;
        _validator = validator;
    }

    public async Task<PagedResult<AttributeValueListItemDto>> GetPagedAsync(
     int storeId,
     int? attributeId,
     bool? status,
     string? search,
     int page,
     int pageSize,
     CancellationToken ct = default)
    {
        var (items, total) = await _repo.GetPagedAsync(
            storeId,
            attributeId,
            status,
            search,
            page,
            pageSize,
            ct);

        return new PagedResult<AttributeValueListItemDto>
        {
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            Items = items.Select(static item => item.ToListItemDto()).ToList()
        };
    }

    public async Task<Result<AttributeValueEditDto>> GetForEditAsync(
        int storeId,
        int id,
        CancellationToken ct = default)
    {
        if (id <= 0)
            return Result.Failure<AttributeValueEditDto>(AttributeValueErrors.InvalidId);

        var entity = await _repo.GetByIdAsync(storeId, id, ct);
        if (entity == null)
            return Result.Failure<AttributeValueEditDto>(AttributeValueErrors.NotFound);

        return Result.Success(entity.ToEditDto());
    }

    public async Task<Result<int>> CreateAsync(
        int storeId,
        CreateAttributeValueRequest dto,
        int? userId,
        CancellationToken ct = default)
    {
        if (dto == null)
            return Result.Failure<int>(AttributeValueErrors.InvalidInput);

        if (dto.AttributeId <= 0)
            return Result.Failure<int>(AttributeValueErrors.InvalidAttribute);

        var attr = await _attrRepo.GetByIdAsync(storeId, dto.AttributeId, ct);
        if (attr == null)
            return Result.Failure<int>(AttributeValueErrors.AttributeNotFound);

        var editDto = dto.ToEditDto();
        Normalize(editDto);

        if (string.IsNullOrWhiteSpace(editDto.Code))
            editDto.Code = await GenerateCodeAsync(storeId, dto.AttributeId, ct);

        var validationResult = await _validator.ValidateAsync(editDto, ct);
        if (!validationResult.IsValid)
            return validationResult.ToFailureResult<int>();

        if (await _repo.ExistsCodeAsync(storeId, dto.AttributeId, editDto.Code, null, ct))
            return Result.Failure<int>(AttributeValueErrors.DuplicateCode);

        if (await _repo.ExistsNameAsync(storeId, dto.AttributeId, editDto.Name, null, ct))
            return Result.Failure<int>(AttributeValueErrors.DuplicateName);

        var entity = editDto.ToEntity();
        entity.StoreId = storeId;
        entity.SortOrder = 0;
        entity.CreatedAtUtc = DateTime.UtcNow;
        entity.CreatedBy = userId;

        try
        {
            await _repo.AddAsync(entity, ct);
            await _repo.SaveChangesAsync(ct);

            if (entity.Id <= 0)
                return Result.Failure<int>(AttributeValueErrors.CreateFailed);

            return Result.Success(entity.Id);
        }
        catch
        {
            return Result.Failure<int>(AttributeValueErrors.CreateFailed);
        }
    }

    public async Task<Result> UpdateAsync(
        int storeId,
        UpdateAttributeValueRequest dto,
        int? userId,
        CancellationToken ct = default)
    {
        if (dto == null || dto.Id <= 0)
            return Result.Failure(AttributeValueErrors.InvalidId);

        var entity = await _repo.GetByIdAsync(storeId, dto.Id, ct);
        if (entity == null)
            return Result.Failure(AttributeValueErrors.NotFound);

        if (dto.AttributeId <= 0)
            return Result.Failure(AttributeValueErrors.InvalidAttribute);

        var attr = await _attrRepo.GetByIdAsync(storeId, dto.AttributeId, ct);
        if (attr == null)
            return Result.Failure(AttributeValueErrors.AttributeNotFound);

        var editDto = dto.ToEditDto();
        Normalize(editDto);

        var validationResult = await _validator.ValidateAsync(editDto, ct);
        if (!validationResult.IsValid)
            return validationResult.ToFailureResult();

        if (await _repo.ExistsCodeAsync(storeId, dto.AttributeId, editDto.Code, dto.Id, ct))
            return Result.Failure(AttributeValueErrors.DuplicateCode);

        if (await _repo.ExistsNameAsync(storeId, dto.AttributeId, editDto.Name, dto.Id, ct))
            return Result.Failure(AttributeValueErrors.DuplicateName);

        entity.RowVersion = dto.RowVersion;
        entity.AttributeId = editDto.AttributeId;
        entity.Code = editDto.Code;
        entity.Name = editDto.Name;
        entity.Status = editDto.Status;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.UpdatedBy = userId;

        try
        {
            await _repo.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (Exception ex) when (ex.GetType().Name == "DbUpdateConcurrencyException")
        {
            return Result.Failure(AttributeValueErrors.ConcurrencyConflict);
        }
        catch
        {
            return Result.Failure(AttributeValueErrors.UpdateFailed);
        }
    }

    public async Task<Result<bool>> ToggleStatusAsync(
        int storeId,
        int id,
        int? userId,
        CancellationToken ct = default)
    {
        if (id <= 0)
            return Result.Failure<bool>(AttributeValueErrors.InvalidId);

        var entity = await _repo.GetByIdAsync(storeId, id, ct);
        if (entity == null)
            return Result.Failure<bool>(AttributeValueErrors.NotFound);

        entity.Status = !entity.Status;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.UpdatedBy = userId;

        try
        {
            await _repo.SaveChangesAsync(ct);
            return Result.Success(entity.Status);
        }
        catch
        {
            return Result.Failure<bool>(AttributeValueErrors.ToggleStatusFailed);
        }
    }

    public async Task<Result> SoftDeleteAsync(
      int storeId,
      int id,
      int? userId,
      CancellationToken ct = default)
    {
        if (id <= 0)
            return Result.Failure(AttributeValueErrors.InvalidId);

        var entity = await _repo.GetByIdAsync(storeId, id, ct);
        if (entity == null)
            return Result.Failure(AttributeValueErrors.NotFound);

        var isUsed = await _repo.IsUsedAsync(storeId, id, ct);
        if (isUsed)
            return Result.Failure(AttributeValueErrors.ValueIsUsed);

        entity.IsDeleted = true;
        entity.DeletedAtUtc = DateTime.UtcNow;
        entity.DeletedBy = userId;

        try
        {
            await _repo.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch
        {
            return Result.Failure(AttributeValueErrors.DeleteFailed);
        }
    }

    private static void Normalize(AttributeValueEditDto dto)
    {
        dto.Code = (dto.Code ?? string.Empty).Trim().ToUpperInvariant();
        dto.Name = (dto.Name ?? string.Empty).Trim();
    }

    private async Task<string> GenerateCodeAsync(int storeId, int attributeId, CancellationToken ct)
    {
        for (var i = 0; i < 50; i++)
        {
            var code = "VAL" + Random.Shared.Next(100000, 999999);
            if (!await _repo.ExistsCodeAsync(storeId, attributeId, code, null, ct))
                return code;
        }

        return "VAL" + DateTime.UtcNow.ToString("yyMMddHHmmss");
    }
}