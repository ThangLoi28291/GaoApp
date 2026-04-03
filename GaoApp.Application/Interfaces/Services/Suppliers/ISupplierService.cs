using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Common;
using GaoApp.Application.DTOs.Suppliers;

namespace GaoApp.Application.Interfaces.Services.Suppliers;

public interface ISupplierService
{
    Task<PagedResult<SupplierListItemDto>> GetPagedAsync(
        int storeId,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<Result<SupplierEditDto>> GetForEditAsync(
        int storeId,
        int id,
        CancellationToken ct = default);

    Task<Result<int>> CreateAsync(
        int storeId,
        CreateSupplierRequest dto,
        int? userId,
        CancellationToken ct = default);

    Task<Result> UpdateAsync(
        int storeId,
        UpdateSupplierRequest dto,
        int? userId,
        CancellationToken ct = default);

    Task<Result> ToggleStatusAsync(
        int storeId,
        int id,
        int? userId,
        CancellationToken ct = default);

    Task<Result> SoftDeleteAsync(
        int storeId,
        int id,
        int? userId,
        CancellationToken ct = default);

    Task<List<Select2OptionDto>> SearchSelect2Async(
        int storeId,
        string? term,
        CancellationToken ct = default);
}