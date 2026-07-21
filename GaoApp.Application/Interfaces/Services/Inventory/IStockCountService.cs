using GaoApp.Application.DTOs.Inventory;

namespace GaoApp.Application.Interfaces.Services.Inventory;

/// <summary>
/// Service xử lý phiếu kiểm kê.
///
/// Ghi chú kiến trúc barcode:
/// - ProductVariant không còn Barcode
/// - Nếu line kiểm kê cần snapshot barcode để hiển thị / đối chiếu,
///   service sẽ tự resolve barcode đại diện từ:
///   ProductUnitConversion + ProductVariantUnitBarcode
/// </summary>
public interface IStockCountService
{
    Task<List<StockCountDocumentListItemDto>> GetListAsync(CancellationToken ct = default);

    Task<int> CreateAsync(CreateStockCountDocumentRequest request, CancellationToken ct = default);

    Task<StockCountDocumentDto?> GetDetailAsync(int id, CancellationToken ct = default);

    Task UpdateHeaderAsync(UpdateStockCountDocumentHeaderRequest request, CancellationToken ct = default);

    Task<int> AddLineAsync(int stockCountDocumentId, AddStockCountLineRequest request, CancellationToken ct = default);

    Task UpdateLineAsync(int lineId, UpdateStockCountLineRequest request, CancellationToken ct = default);

    Task DeleteLineAsync(int lineId, CancellationToken ct = default);

    Task ConfirmAsync(int stockCountDocumentId, CancellationToken ct = default);

    Task SubmitForApprovalAsync(int stockCountDocumentId, CancellationToken ct = default);

    Task RejectAsync(int stockCountDocumentId, CancellationToken ct = default);

    Task RefreshSystemQtyAsync(int stockCountDocumentId, CancellationToken ct = default);
}