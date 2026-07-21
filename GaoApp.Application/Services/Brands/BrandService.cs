using AutoMapper;
using FluentValidation;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Brands;
using GaoApp.Application.Features.Brands;
using GaoApp.Application.Interfaces.Repositories.Brands;
using GaoApp.Application.Interfaces.Services.Brands;
using GaoApp.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace GaoApp.Application.Services.Brands;

public sealed class BrandService : IBrandService
{
    private readonly IBrandRepository _repo;
    private readonly IMapper _mapper;
    private readonly IValidator<BrandEditDto> _validator;
    private readonly ILogger<BrandService> _logger;

    public BrandService(
        IBrandRepository repo,
        IMapper mapper,
        IValidator<BrandEditDto> validator,
        ILogger<BrandService> logger)
    {
        _repo = repo;
        _mapper = mapper;
        _validator = validator;
        _logger = logger;
    }

    public async Task<PagedResult<BrandListItemDto>> GetPagedAsync(
        int storeId,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var (items, totalItems) = await _repo.GetPagedAsync(storeId, search, page, pageSize, ct);

        return new PagedResult<BrandListItemDto>
        {
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            Items = _mapper.Map<List<BrandListItemDto>>(items)
        };
    }

    public async Task<Result<BrandEditDto>> GetForEditAsync(
        int storeId,
        int id,
        CancellationToken ct = default)
    {
        if (id <= 0)
            return Result.Failure<BrandEditDto>(BrandErrors.InvalidId);

        var entity = await _repo.GetByIdAsync(storeId, id, ct);
        if (entity == null)
            return Result.Failure<BrandEditDto>(BrandErrors.NotFound);

        var dto = _mapper.Map<BrandEditDto>(entity);
        return Result.Success(dto);
    }

    public async Task<Result<int>> CreateAsync(
        int storeId,
        CreateBrandRequest dto,
        int? userId,
        CancellationToken ct = default)
    {
        if (dto == null)
            return Result.Failure<int>(BrandErrors.InvalidInput);

        // Map request -> dto nội bộ để gom normalize + validate vào 1 luồng
        var editDto = _mapper.Map<BrandEditDto>(dto);

        Normalize(editDto);

        // Create được phép không nhập code => tự sinh code trước khi validate
        if (string.IsNullOrWhiteSpace(editDto.Code))
            editDto.Code = await GenerateCodeAsync(storeId, ct);

        var validationResult = await _validator.ValidateAsync(editDto, ct);
        if (!validationResult.IsValid)
            return validationResult.ToFailureResult<int>();

        if (await _repo.ExistsCodeAsync(storeId, editDto.Code, null, ct))
            return Result.Failure<int>(BrandErrors.DuplicateCode);

        if (await _repo.ExistsNameAsync(storeId, editDto.Name, null, ct))
            return Result.Failure<int>(BrandErrors.DuplicateName);

        var entity = _mapper.Map<Brand>(editDto);
        entity.StoreId = storeId;

        try
        {
            await _repo.AddAsync(entity, ct);
            await _repo.SaveChangesAsync(ct);

            if (entity.Id <= 0)
                return Result.Failure<int>(BrandErrors.CreateFailed);

            return Result.Success(entity.Id);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Create brand failed. StoreId={StoreId}", storeId);
            return Result.Failure<int>(BrandErrors.CreateFailed);
        }
    }

    public async Task<Result> UpdateAsync(
        int storeId,
        UpdateBrandRequest dto,
        int? userId,
        CancellationToken ct = default)
    {
        if (dto == null || dto.Id <= 0)
            return Result.Failure(BrandErrors.InvalidId);

        var entity = await _repo.GetByIdAsync(storeId, dto.Id, ct);
        if (entity == null)
            return Result.Failure(BrandErrors.NotFound);

        var editDto = _mapper.Map<BrandEditDto>(dto);
        Normalize(editDto);

        var validationResult = await _validator.ValidateAsync(editDto, ct);
        if (!validationResult.IsValid)
            return validationResult.ToFailureResult();

        if (await _repo.ExistsCodeAsync(storeId, editDto.Code, dto.Id, ct))
            return Result.Failure(BrandErrors.DuplicateCode);

        if (await _repo.ExistsNameAsync(storeId, editDto.Name, dto.Id, ct))
            return Result.Failure(BrandErrors.DuplicateName);

        // Gán RowVersion để EF check concurrency
        entity.RowVersion = dto.RowVersion;

        // Update entity an toàn, chỉ gán field được phép sửa
        entity.Code = editDto.Code;
        entity.Name = editDto.Name;
        entity.Description = editDto.Description;
        entity.IsActive = editDto.Status;

        try
        {
            await _repo.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex.GetType().Name == "DbUpdateConcurrencyException")
        {
            _logger.LogWarning(ex, "Brand concurrency conflict. StoreId={StoreId}; BrandId={BrandId}", storeId, dto.Id);
            return Result.Failure(BrandErrors.ConcurrencyConflict);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update brand failed. StoreId={StoreId}; BrandId={BrandId}", storeId, dto.Id);
            return Result.Failure(BrandErrors.UpdateFailed);
        }
    }

    public async Task<Result<bool>> ToggleStatusAsync(
        int storeId,
        int id,
        int? userId,
        CancellationToken ct = default)
    {
        if (id <= 0)
            return Result.Failure<bool>(BrandErrors.InvalidId);

        var entity = await _repo.GetByIdAsync(storeId, id, ct);
        if (entity == null)
            return Result.Failure<bool>(BrandErrors.NotFound);

        entity.IsActive = !entity.IsActive;

        try
        {
            await _repo.SaveChangesAsync(ct);
            return Result.Success(entity.IsActive);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Toggle brand status failed. StoreId={StoreId}; BrandId={BrandId}", storeId, id);
            return Result.Failure<bool>(BrandErrors.ToggleStatusFailed);
        }
    }

    public async Task<Result> SoftDeleteAsync(
        int storeId,
        int id,
        int? userId,
        CancellationToken ct = default)
    {
        if (id <= 0)
            return Result.Failure(BrandErrors.InvalidId);

        var entity = await _repo.GetByIdAsync(storeId, id, ct);
        if (entity == null)
            return Result.Failure(BrandErrors.NotFound);

        try
        {
            _repo.Remove(entity);
            await _repo.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete brand failed. StoreId={StoreId}; BrandId={BrandId}", storeId, id);
            return Result.Failure(BrandErrors.DeleteFailed);
        }
    }

    /// <summary>
    /// Chuẩn hóa dữ liệu đầu vào trước validate + duplicate check.
    /// </summary>
    private static void Normalize(BrandEditDto dto)
    {
        dto.Code = (dto.Code ?? string.Empty).Trim().ToUpperInvariant();
        dto.Name = (dto.Name ?? string.Empty).Trim();
        dto.Description = string.IsNullOrWhiteSpace(dto.Description)
            ? null
            : dto.Description.Trim();
    }

    /// <summary>
    /// Tự sinh code nếu create không nhập mã.
    /// Không đụng repository cũ.
    /// </summary>
    private async Task<string> GenerateCodeAsync(int storeId, CancellationToken ct)
    {
        for (var i = 0; i < 50; i++)
        {
            var code = "BRD" + Random.Shared.Next(100000, 999999);
            if (!await _repo.ExistsCodeAsync(storeId, code, null, ct))
                return code;
        }

        return "BRD" + DateTime.UtcNow.ToString("yyMMddHHmmss");
    }
}
