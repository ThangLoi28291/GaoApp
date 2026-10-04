using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Customers;
using GaoApp.Application.Interfaces.Repositories.Customers;
using GaoApp.Application.Interfaces.Services.Customers;
using GaoApp.Domain.Constants;
using GaoApp.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace GaoApp.Application.Services.Customers;

public sealed class CustomerManagementService : ICustomerManagementService
{
    private static readonly Error NotFound = Error.NotFound("Không tìm thấy khách hàng trong cửa hàng hiện tại.");
    private static readonly Error DuplicateCode = Error.Conflict("Mã khách hàng đã tồn tại trong cửa hàng.");
    private static readonly Error DuplicatePhone = Error.Conflict("Số điện thoại đã tồn tại trong cửa hàng.");
    private static readonly Error InvalidTier = Error.Validation("Customer.PriceTier", "Nhóm giá khách hàng không hợp lệ.");
    private static readonly Error SaveFailed = Error.Failure("Không thể lưu khách hàng. Vui lòng thử lại.");
    private static readonly Error ConcurrencyConflict = Error.Conflict("Dữ liệu đã thay đổi. Vui lòng tải lại trước khi lưu.");

    private readonly ICustomerManagementRepository _repository;
    private readonly ICurrentStore _currentStore;
    private readonly ILogger<CustomerManagementService> _logger;

    public CustomerManagementService(
        ICustomerManagementRepository repository,
        ICurrentStore currentStore,
        ILogger<CustomerManagementService> logger)
    {
        _repository = repository;
        _currentStore = currentStore;
        _logger = logger;
    }

    public async Task<CustomerManagementPageDto> GetPageAsync(
        CustomerManagementQueryRequest request,
        CancellationToken ct = default)
    {
        NormalizeQuery(request);
        var storeId = _currentStore.StoreId;
        return new CustomerManagementPageDto
        {
            Summary = await _repository.GetSummaryAsync(storeId, request, ct),
            Paged = await _repository.GetPageAsync(storeId, request, ct)
        };
    }

    public Task<CustomerQuickViewDto?> GetQuickViewAsync(int id, CancellationToken ct = default)
        => _repository.GetQuickViewAsync(_currentStore.StoreId, id, ct);

    public async Task<Result<CustomerEditDto>> GetForEditAsync(int id, CancellationToken ct = default)
    {
        var entity = await _repository.GetByIdAsync(_currentStore.StoreId, id, ct);
        if (entity is null) return Result.Failure<CustomerEditDto>(NotFound);

        return Result.Success(new CustomerEditDto
        {
            Id = entity.Id,
            Name = entity.Name,
            Code = entity.Code,
            Phone = entity.Phone,
            Email = entity.Email,
            TaxCode = entity.TaxCode,
            Address = entity.Address,
            Note = entity.Note,
            PriceTier = entity.PriceTier,
            HaveDebt = entity.HaveDebt,
            AskBeforePrintingReceipt = entity.AskBeforePrintingReceipt,
            IsActive = entity.IsActive,
            RowVersion = entity.RowVersion is { Length: > 0 }
                ? Convert.ToBase64String(entity.RowVersion)
                : null
        });
    }

    public async Task<Result<int>> CreateAsync(CustomerEditDto dto, CancellationToken ct = default)
    {
        Normalize(dto);
        var validation = Validate(dto);
        if (validation is not null) return Result.Failure<int>(validation);

        var storeId = _currentStore.StoreId;
        var duplicate = await CheckDuplicatesAsync(storeId, dto, null, ct);
        if (duplicate is not null) return Result.Failure<int>(duplicate);

        var entity = new Customer
        {
            StoreId = storeId,
            Name = dto.Name,
            Code = dto.Code,
            Phone = dto.Phone,
            Email = dto.Email,
            TaxCode = dto.TaxCode,
            Address = dto.Address,
            Note = dto.Note,
            PriceTier = dto.PriceTier,
            HaveDebt = dto.HaveDebt,
            AskBeforePrintingReceipt = dto.AskBeforePrintingReceipt,
            IsActive = dto.IsActive
        };

        try
        {
            await _repository.AddAsync(entity, ct);
            await _repository.SaveChangesAsync(ct);
            return Result.Success(entity.Id);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Creating customer failed for StoreId={StoreId}", storeId);
            return Result.Failure<int>(SaveFailed);
        }
    }

    public async Task<Result> UpdateAsync(CustomerEditDto dto, CancellationToken ct = default)
    {
        Normalize(dto);
        var validation = Validate(dto);
        if (dto.Id <= 0 || validation is not null)
            return Result.Failure(validation ?? NotFound);

        var storeId = _currentStore.StoreId;
        var entity = await _repository.GetByIdAsync(storeId, dto.Id, ct);
        if (entity is null) return Result.Failure(NotFound);

        if (!string.IsNullOrWhiteSpace(dto.RowVersion) && entity.RowVersion is { Length: > 0 })
        {
            try
            {
                if (!entity.RowVersion.SequenceEqual(Convert.FromBase64String(dto.RowVersion)))
                    return Result.Failure(ConcurrencyConflict);
            }
            catch (FormatException)
            {
                return Result.Failure(ConcurrencyConflict);
            }
        }

        var duplicate = await CheckDuplicatesAsync(storeId, dto, dto.Id, ct);
        if (duplicate is not null) return Result.Failure(duplicate);

        entity.Name = dto.Name;
        entity.Code = dto.Code;
        entity.Phone = dto.Phone;
        entity.Email = dto.Email;
        entity.TaxCode = dto.TaxCode;
        entity.Address = dto.Address;
        entity.Note = dto.Note;
        entity.PriceTier = dto.PriceTier;
        entity.HaveDebt = dto.HaveDebt;
        entity.AskBeforePrintingReceipt = dto.AskBeforePrintingReceipt;
        entity.IsActive = dto.IsActive;

        return await SaveAsync(storeId, dto.Id, ct);
    }

    public async Task<Result<bool>> ToggleActiveAsync(int id, CancellationToken ct = default)
    {
        var storeId = _currentStore.StoreId;
        var entity = await _repository.GetByIdAsync(storeId, id, ct);
        if (entity is null) return Result.Failure<bool>(NotFound);

        entity.IsActive = !entity.IsActive;
        var result = await SaveAsync(storeId, id, ct);
        return result.IsSuccess
            ? Result.Success(entity.IsActive)
            : Result.Failure<bool>(result.Error);
    }

    private async Task<Result> SaveAsync(int storeId, int id, CancellationToken ct)
    {
        try
        {
            await _repository.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Saving customer failed for StoreId={StoreId}, CustomerId={CustomerId}", storeId, id);
            return Result.Failure(SaveFailed);
        }
    }

    private async Task<Error?> CheckDuplicatesAsync(
        int storeId,
        CustomerEditDto dto,
        int? excludeId,
        CancellationToken ct)
    {
        if (dto.Code is not null && await _repository.ExistsCodeAsync(storeId, dto.Code, excludeId, ct))
            return DuplicateCode;
        if (dto.Phone is not null && await _repository.ExistsPhoneAsync(storeId, dto.Phone, excludeId, ct))
            return DuplicatePhone;
        return null;
    }

    private static Error? Validate(CustomerEditDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return Error.Validation("Customer.Name", "Vui lòng nhập tên khách hàng.");
        if (dto.PriceTier is not CustomerPriceTiers.Retail and not CustomerPriceTiers.Wholesale)
            return InvalidTier;
        return null;
    }

    private static void NormalizeQuery(CustomerManagementQueryRequest request)
    {
        request.SearchString = Clean(request.SearchString);
        request.PriceTier = Clean(request.PriceTier)?.ToUpperInvariant();
        if (request.PriceTier == CustomerManagementPriceTiers.All) request.PriceTier = null;
        request.Page = Math.Max(1, request.Page);
        request.PageSize = request.PageSize is 10 or 20 or 50 or 100 ? request.PageSize : 20;
    }

    private static void Normalize(CustomerEditDto dto)
    {
        dto.Name = (dto.Name ?? string.Empty).Trim();
        dto.Code = Clean(dto.Code)?.ToUpperInvariant();
        dto.Phone = Clean(dto.Phone);
        dto.Email = Clean(dto.Email)?.ToLowerInvariant();
        dto.TaxCode = Clean(dto.TaxCode)?.ToUpperInvariant();
        dto.Address = Clean(dto.Address);
        dto.Note = Clean(dto.Note);
        dto.PriceTier = (dto.PriceTier ?? string.Empty).Trim().ToUpperInvariant();
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
