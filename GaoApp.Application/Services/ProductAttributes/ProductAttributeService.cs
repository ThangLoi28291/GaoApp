using AutoMapper;
using FluentValidation;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.ProductAttributes;
using GaoApp.Application.Features.ProductAttributes;
using GaoApp.Application.Interfaces.Repositories.ProductAttributes;
using GaoApp.Application.Interfaces.Services.ProductAttributes;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Services.ProductAttributes;

public sealed class ProductAttributeService : IProductAttributeService
{
    private readonly IProductAttributeRepository _repo;
    private readonly IMapper _mapper;
    private readonly IValidator<ProductAttributeEditDto> _validator;

    public ProductAttributeService(
        IProductAttributeRepository repo,
        IMapper mapper,
        IValidator<ProductAttributeEditDto> validator)
    {
        _repo = repo;
        _mapper = mapper;
        _validator = validator;
    }

    public Task<List<ProductAttribute>> GetAllAsync(int storeId, CancellationToken ct = default)
        => _repo.GetAllAsync(storeId, ct);

    public async Task<PagedResult<ProductAttributeListItemDto>> GetPagedAsync(
        int storeId, string? search, int page, int pageSize, CancellationToken ct = default)
    {
        var (items, total) = await _repo.GetPagedAsync(storeId, search, page, pageSize, ct);

        return new PagedResult<ProductAttributeListItemDto>
        {
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            Items = _mapper.Map<List<ProductAttributeListItemDto>>(items)
        };
    }

    public async Task<Result<ProductAttributeEditDto>> GetForEditAsync(
        int storeId,
        int id,
        CancellationToken ct = default)
    {
        if (id <= 0)
            return Result.Failure<ProductAttributeEditDto>(ProductAttributeErrors.InvalidId);

        var entity = await _repo.GetByIdAsync(storeId, id, ct);
        if (entity == null)
            return Result.Failure<ProductAttributeEditDto>(ProductAttributeErrors.NotFound);

        return Result.Success(_mapper.Map<ProductAttributeEditDto>(entity));
    }

    public async Task<Result<int>> CreateAsync(
        int storeId,
        CreateProductAttributeRequest dto,
        int? userId,
        CancellationToken ct = default)
    {
        if (dto == null)
            return Result.Failure<int>(ProductAttributeErrors.InvalidInput);

        var editDto = _mapper.Map<ProductAttributeEditDto>(dto);
        Normalize(editDto);

        if (string.IsNullOrWhiteSpace(editDto.Code))
            editDto.Code = await GenerateCodeAsync(storeId, ct);

        var validationResult = await _validator.ValidateAsync(editDto, ct);
        if (!validationResult.IsValid)
            return validationResult.ToFailureResult<int>();

        if (await _repo.ExistsCodeAsync(storeId, editDto.Code, null, ct))
            return Result.Failure<int>(ProductAttributeErrors.DuplicateCode);

        if (await _repo.ExistsNameAsync(storeId, editDto.Name, null, ct))
            return Result.Failure<int>(ProductAttributeErrors.DuplicateName);

        var entity = _mapper.Map<ProductAttribute>(editDto);
        entity.StoreId = storeId;
        entity.SortOrder = 0;
        entity.CreatedAtUtc = DateTime.UtcNow;
        entity.CreatedBy = userId;

        try
        {
            await _repo.AddAsync(entity, ct);
            await _repo.SaveChangesAsync(ct);

            if (entity.Id <= 0)
                return Result.Failure<int>(ProductAttributeErrors.CreateFailed);

            return Result.Success(entity.Id);
        }
        catch
        {
            return Result.Failure<int>(ProductAttributeErrors.CreateFailed);
        }
    }

    public async Task<Result> UpdateAsync(
        int storeId,
        UpdateProductAttributeRequest dto,
        int? userId,
        CancellationToken ct = default)
    {
        if (dto == null || dto.Id <= 0)
            return Result.Failure(ProductAttributeErrors.InvalidId);

        var entity = await _repo.GetByIdAsync(storeId, dto.Id, ct);
        if (entity == null)
            return Result.Failure(ProductAttributeErrors.NotFound);

        var editDto = _mapper.Map<ProductAttributeEditDto>(dto);
        Normalize(editDto);

        var validationResult = await _validator.ValidateAsync(editDto, ct);
        if (!validationResult.IsValid)
            return validationResult.ToFailureResult();

        if (await _repo.ExistsCodeAsync(storeId, editDto.Code, dto.Id, ct))
            return Result.Failure(ProductAttributeErrors.DuplicateCode);

        if (await _repo.ExistsNameAsync(storeId, editDto.Name, dto.Id, ct))
            return Result.Failure(ProductAttributeErrors.DuplicateName);

        entity.RowVersion = dto.RowVersion;
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
            return Result.Failure(ProductAttributeErrors.ConcurrencyConflict);
        }
        catch
        {
            return Result.Failure(ProductAttributeErrors.UpdateFailed);
        }
    }

    public async Task<Result<bool>> ToggleStatusAsync(
        int storeId,
        int id,
        int? userId,
        CancellationToken ct = default)
    {
        if (id <= 0)
            return Result.Failure<bool>(ProductAttributeErrors.InvalidId);

        var entity = await _repo.GetByIdAsync(storeId, id, ct);
        if (entity == null)
            return Result.Failure<bool>(ProductAttributeErrors.NotFound);

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
            return Result.Failure<bool>(ProductAttributeErrors.ToggleStatusFailed);
        }
    }

    public async Task<Result> SoftDeleteAsync(
        int storeId,
        int id,
        int? userId,
        CancellationToken ct = default)
    {
        if (id <= 0)
            return Result.Failure(ProductAttributeErrors.InvalidId);

        var entity = await _repo.GetByIdAsync(storeId, id, ct);
        if (entity == null)
            return Result.Failure(ProductAttributeErrors.NotFound);

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
            return Result.Failure(ProductAttributeErrors.DeleteFailed);
        }
    }

    private static void Normalize(ProductAttributeEditDto dto)
    {
        dto.Code = (dto.Code ?? string.Empty).Trim().ToUpperInvariant();
        dto.Name = (dto.Name ?? string.Empty).Trim();
    }

    private async Task<string> GenerateCodeAsync(int storeId, CancellationToken ct)
    {
        for (var i = 0; i < 50; i++)
        {
            var code = "ATTR" + Random.Shared.Next(100000, 999999);
            if (!await _repo.ExistsCodeAsync(storeId, code, null, ct))
                return code;
        }

        return "ATTR" + DateTime.UtcNow.ToString("yyMMddHHmmss");
    }
}