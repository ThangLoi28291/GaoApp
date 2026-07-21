using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Products;

namespace GaoApp.Application.Interfaces.Services.Products;

public interface IBarcodeHistoryService
{
    Task<PagedResult<BarcodeHistoryRowDto>> GetPagedAsync(
        BarcodeHistoryQueryRequest request,
        CancellationToken ct = default);
}