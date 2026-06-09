using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.AttributeValues;

namespace GaoApp.Application.Interfaces.Services.AttributeValues;

public interface IAttributeValueService
{
    Task<PagedResult<AttributeValueListItemDto>> GetPagedAsync(
     int storeId,
     int? attributeId,
     bool? status,
     string? search,
     int page,
     int pageSize,
     CancellationToken ct = default);

    Task<Result<AttributeValueEditDto>> GetForEditAsync(
        int storeId,
        int id,
        CancellationToken ct = default);

    Task<Result<int>> CreateAsync(
        int storeId,
        CreateAttributeValueRequest dto,
        int? userId,
        CancellationToken ct = default);

    Task<Result> UpdateAsync(
        int storeId,
        UpdateAttributeValueRequest dto,
        int? userId,
        CancellationToken ct = default);

    Task<Result<bool>> ToggleStatusAsync(
        int storeId,
        int id,
        int? userId,
        CancellationToken ct = default);

    Task<Result> SoftDeleteAsync(
        int storeId,
        int id,
        int? userId,
        CancellationToken ct = default);
   
}