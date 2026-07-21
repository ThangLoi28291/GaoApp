using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Inventory;

namespace GaoApp.Application.Interfaces.Services.Inventory;

/// <summary>
/// Service nghiệp vụ chuyển kho.
///
/// Ghi chú kiến trúc barcode:
/// - ProductVariant không còn Barcode
/// - Nếu line chuyển kho cần snapshot barcode để hiển thị / đối chiếu,
///   service sẽ tự resolve barcode đại diện từ:
///   ProductUnitConversion + ProductVariantUnitBarcode
/// </summary>
public interface IStockTransferService
{
    Task<PagedResult<StockTransferDocumentListItemDto>> GetListAsync(
        int page,
        int pageSize,
        string? keyword,
        int? fromWarehouseId,
        int? toWarehouseId,
        int? status,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken ct = default);

    Task<int> CreateAsync(CreateStockTransferDocumentRequest request, CancellationToken ct = default);

    Task<StockTransferDocumentDto?> GetDetailAsync(int id, CancellationToken ct = default);

    Task UpdateHeaderAsync(int id, UpdateStockTransferHeaderRequest request, CancellationToken ct = default);

    Task<int> AddLineAsync(int documentId, AddStockTransferLineRequest request, CancellationToken ct = default);

    Task UpdateLineAsync(int lineId, UpdateStockTransferLineRequest request, CancellationToken ct = default);

    Task DeleteLineAsync(int lineId, CancellationToken ct = default);

    Task SubmitAsync(int id, CancellationToken ct = default);

    Task RejectAsync(int id, string? reason, CancellationToken ct = default);

    Task ConfirmAsync(int id, CancellationToken ct = default);
}