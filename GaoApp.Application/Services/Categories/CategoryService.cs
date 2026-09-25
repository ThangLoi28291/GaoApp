using FluentValidation;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Categories;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Application.Interfaces.Repositories.Categories;
using GaoApp.Application.Interfaces.Services.Categories;
using GaoApp.Application.Mappings.Categories;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Services.Categories;

/// <summary>
/// Service nghiệp vụ Category theo pattern mới:
/// - Validation bằng FluentValidation
/// - Kết quả trả về bằng Result / Result<T>
/// - Mapping bằng C# thủ công, tường minh
///
/// Giữ nguyên repository hiện tại để không phá code cũ.
/// </summary>
public class CategoryService : ICategoryService
{
    private readonly ICategoryRepository _repository;
    private readonly ICurrentStore _currentStore;
    private readonly ICurrentUser _currentUser;
    private readonly IValidator<CategoryEditDto> _validator;

    public CategoryService(
        ICategoryRepository repository,
        ICurrentStore currentStore,
        IValidator<CategoryEditDto> validator,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _currentStore = currentStore;
        _validator = validator;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<CategoryListItemDto>> GetPagedAsync(
        string? search,
        int page,
        int pageSize,
        CancellationToken ct = default)
        => await GetPagedAsync(search, status: null, page, pageSize, ct);

    public async Task<PagedResult<CategoryListItemDto>> GetPagedAsync(
        string? search,
        bool? status,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var storeId = _currentStore.StoreId;

        var paged = await _repository.GetPagedAsync(storeId, search, status, page, pageSize, ct);

        return new PagedResult<CategoryListItemDto>
        {
            Page = paged.Page,
            PageSize = paged.PageSize,
            TotalItems = paged.TotalItems,
            Items = (paged.Items ?? new List<Category>())
                .Select(static item => item.ToListItemDto())
                .ToList()
        };
    }

    public Task<(int TotalItems, int ActiveItems, int InactiveItems)> GetSummaryAsync(
        CancellationToken ct = default)
        => _repository.GetSummaryAsync(_currentStore.StoreId, ct);

    public async Task<Result<CategoryEditDto>> GetForEditAsync(int id, CancellationToken ct = default)
    {
        var storeId = _currentStore.StoreId;

        var entity = await _repository.GetByIdAsync(storeId, id, ct);
        if (entity == null)
        {
            return Result.Failure<CategoryEditDto>(CategoryErrors.NotFound);
        }

        var dto = entity.ToEditDto();
        return Result.Success(dto);
    }

    public async Task<Result<int>> CreateAsync(CategoryEditDto dto, CancellationToken ct = default)
    {
        var storeId = _currentStore.StoreId;

        // =========================================================
        // 1. Validate input bằng FluentValidation
        // Step 8A.1: trả toàn bộ lỗi validation nếu có
        // =========================================================
        var validationResult = await _validator.ValidateAsync(dto, ct);
        if (!validationResult.IsValid)
        {
            return validationResult.ToFailureResult<int>();
        }

        // =========================================================
        // 2. Normalize dữ liệu đầu vào
        // =========================================================
        var normalizedCode = dto.Code?.Trim();
        var normalizedName = dto.Name?.Trim();

        // Validator đã chặn Name rỗng, nhưng vẫn check defensive
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return Result.ValidationFailure<int>(new[]
            {
                new ValidationError(nameof(dto.Name), "Tên danh mục là bắt buộc.")
            });
        }

        // =========================================================
        // 3. Kiểm tra parent category nếu có
        // =========================================================
        if (dto.ParentId.HasValue)
        {
            var parent = await _repository.GetByIdAsync(storeId, dto.ParentId.Value, ct);
            if (parent == null)
            {
                return Result.Failure<int>(CategoryErrors.ParentNotFound);
            }
        }

        // =========================================================
        // 4. Check trùng code nếu có nhập
        // =========================================================
        if (!string.IsNullOrWhiteSpace(normalizedCode))
        {
            var codeExists = await _repository.ExistsCodeAsync(
                storeId,
                normalizedCode,
                ignoreId: null,
                ct);

            if (codeExists)
            {
                return Result.Failure<int>(CategoryErrors.DuplicateCode);
            }
        }

        // =========================================================
        // 5. Check trùng tên
        // =========================================================
        var nameExists = await _repository.ExistsNameAsync(
            storeId,
            normalizedName,
            ignoreId: null,
            ct);

        if (nameExists)
        {
            return Result.Failure<int>(CategoryErrors.DuplicateName);
        }

        // =========================================================
        // 6. Map DTO -> Entity
        // =========================================================
        var entity = dto.ToEntity();

        // normalizedCode đã Trim ở trên.
        // Null hoặc rỗng trong create có nghĩa là tự sinh mã.
        var finalCode = normalizedCode is { Length: > 0 }
            ? normalizedCode
            : $"CAT-{DateTime.UtcNow:yyyyMMddHHmmss}";

        //entity.StoreId = storeId;
        entity.Code = finalCode;
        entity.Name = normalizedName;

        // =========================================================
        // 7. Save
        // =========================================================
        var createdId = await _repository.CreateAsync(entity, ct);

        if (createdId <= 0)
        {
            return Result.Failure<int>(CategoryErrors.CreateFailed);
        }

        return Result.Success(createdId);
    }

    public async Task<Result> UpdateAsync(CategoryEditDto dto, CancellationToken ct = default)
    {
        var storeId = _currentStore.StoreId;

        // =========================================================
        // 1. Validate input bằng FluentValidation
        // =========================================================
        var validationResult = await _validator.ValidateAsync(dto, ct);
        if (!validationResult.IsValid)
        {
            return validationResult.ToFailureResult();
        }

        // =========================================================
        // 2. Tìm entity hiện có
        // =========================================================
        var entity = await _repository.GetByIdAsync(storeId, dto.Id, ct);
        if (entity == null)
        {
            return Result.Failure(CategoryErrors.NotFound);
        }

        // =========================================================
        // 3. Normalize dữ liệu
        // =========================================================
        var normalizedCode = dto.Code?.Trim();
        var normalizedName = dto.Name?.Trim();

        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return Result.ValidationFailure(new[]
            {
                new ValidationError(nameof(dto.Name), "Tên danh mục là bắt buộc.")
            });
        }

        // =========================================================
        // 4. Parent nếu có thì phải tồn tại và không được self-parent
        // =========================================================
        if (dto.ParentId.HasValue)
        {
            if (dto.ParentId.Value == dto.Id)
            {
                return Result.Failure(
                    new Error("Category.ParentSelf", "Danh mục không thể chọn chính nó làm cha."));
            }

            var parent = await _repository.GetByIdAsync(storeId, dto.ParentId.Value, ct);
            if (parent == null)
            {
                return Result.Failure(CategoryErrors.ParentNotFound);
            }
        }

        // =========================================================
        // 5. Check trùng code
        // =========================================================
        if (normalizedCode is { Length: > 0 })
        {
            var codeExists = await _repository.ExistsCodeAsync(
                storeId,
                normalizedCode,
                dto.Id,
                ct);

            if (codeExists)
            {
                return Result.Failure(CategoryErrors.DuplicateCode);
            }

            // Code ở màn hình edit là readonly.
            // Request không gửi code thì giữ nguyên mã hiện có.
            entity.Code = normalizedCode;
        }

        // =========================================================
        // 6. Check trùng tên
        // =========================================================
        var nameExists = await _repository.ExistsNameAsync(
            storeId,
            normalizedName,
            dto.Id,
            ct);

        if (nameExists)
        {
            return Result.Failure(CategoryErrors.DuplicateName);
        }

        // =========================================================
        // 7. Update entity
        // Không map đè cả object để tránh ảnh hưởng field không mong muốn
        // =========================================================
        entity.Name = normalizedName;
        entity.ParentId = dto.ParentId;
        entity.SortOrder = dto.SortOrder;
        entity.IsActive = dto.IsActive;

        var updated = await _repository.UpdateAsync(entity, ct);
        if (!updated)
        {
            return Result.Failure(CategoryErrors.UpdateFailed);
        }

        return Result.Success();
    }

    public async Task<Result> ToggleStatusAsync(int id, CancellationToken ct = default)
    {
        var storeId = _currentStore.StoreId;
        var userId = _currentUser.UserId;

        var success = await _repository.ToggleStatusAsync(storeId, id, userId, ct);
        if (!success)
        {
            return Result.Failure(CategoryErrors.ToggleStatusFailed);
        }

        return Result.Success();
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken ct = default)
    {
        var storeId = _currentStore.StoreId;
        var userId = _currentUser.UserId;

        var success = await _repository.SoftDeleteAsync(storeId, id, userId, ct);
        if (!success)
        {
            return Result.Failure(CategoryErrors.DeleteFailed);
        }

        return Result.Success();
    }

    public async Task<List<(int Id, string Name)>> GetParentOptionsAsync(int? excludeId, CancellationToken ct = default)
    {
        var storeId = _currentStore.StoreId;

        var items = await _repository.GetParentOptionsAsync(storeId, excludeId, ct);

        return items
            .Select(x => (x.Id, x.Name))
            .ToList();
    }
}
