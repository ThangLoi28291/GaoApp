using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Interfaces.Repositories.Inventory;

/// <summary>
/// Repository đọc/ghi valuation entries phục vụ costing / provisional / revaluation.
///
/// Ghi chú:
/// - InventoryTransaction thiên về qty movement
/// - InventoryValuationEntry thiên về cost/value movement
/// - SalesReturn/Void mức 2 sẽ reverse theo source fragment gốc, không tính lại theo average cost hiện tại
/// - Repository này chỉ hỗ trợ đọc/ghi dữ liệu phục vụ orchestration business flow,
///   không chứa costing logic
/// </summary>
public interface IInventoryValuationEntryRepository
{
    /// <summary>
    /// Thêm nhiều valuation entries cùng lúc.
    /// Thường dùng khi 1 movement sinh nhiều fragment valuation.
    /// </summary>
    Task AddRangeAsync(IEnumerable<InventoryValuationEntry> entries, CancellationToken ct = default);

    /// <summary>
    /// Lấy valuation entries theo reference chung.
    /// Dùng cho:
    /// - nghiệp vụ cũ
    /// - màn hình audit
    /// - đối soát entry theo document/reference
    /// </summary>
    Task<List<InventoryValuationEntry>> GetByReferenceAsync(
        InventoryReferenceType referenceType,
        string referenceId,
        int? referenceLineId,
        CancellationToken ct = default);

    /// <summary>
    /// Lấy toàn bộ valuation entries của 1 inventory transaction.
    /// Hữu ích khi cần xem 1 movement đã sinh ra các fragment nào.
    /// </summary>
    Task<List<InventoryValuationEntry>> GetByInventoryTransactionIdAsync(
        int inventoryTransactionId,
        CancellationToken ct = default);

    Task<List<InventoryValuationEntry>> GetByInventoryTransactionIdAsync(
        int storeId,
        int inventoryTransactionId,
        CancellationToken ct = default);

    /// <summary>
    /// Lấy các outbound valuation entry gốc của 1 order line.
    ///
    /// Dùng cho:
    /// - SalesReturn mức 2
    /// - SaleVoid mirror theo fragment gốc
    ///
    /// Chỉ lấy các entry thuộc sale issue outbound thực sự.
    /// Không lấy inbound return/void, không lấy entry reverse sau này.
    /// </summary>
    Task<List<InventoryValuationEntry>> GetSaleIssueEntriesByOrderLineAsync(
        int orderId,
        int orderLineId,
        CancellationToken ct = default);

    /// <summary>
    /// Lấy toàn bộ valuation entries đang reverse / mirror từ 1 source entry.
    ///
    /// Dùng để tính:
    /// - source fragment đã bị return bao nhiêu
    /// - source fragment đã bị void bao nhiêu
    /// - source fragment còn outstanding bao nhiêu
    ///
    /// Đây là nền cho partial return đúng theo fragment/layer gốc.
    /// </summary>
    Task<List<InventoryValuationEntry>> GetReverseEntriesBySourceEntryIdAsync(
        int sourceValuationEntryId,
        CancellationToken ct = default);

    /// <summary>
    /// Lấy 1 valuation entry theo id.
    /// </summary>
    Task<InventoryValuationEntry?> GetByIdAsync(
        int id,
        CancellationToken ct = default);

    /// <summary>
    /// Kiểm tra đã tồn tại valuation entry theo key reference + subkey hay chưa.
    ///
    /// Dùng để dedupe mức 2 theo fragment.
    /// Ví dụ:
    /// - return line đã mirror source fragment này rồi thì không tạo trùng
    /// - re-run command không sinh duplicate valuation
    /// </summary>
    Task<bool> ExistsByReferenceSubKeyAsync(
        InventoryReferenceType referenceType,
        string referenceId,
        int? referenceLineId,
        string? referenceSubKey,
        InventoryValuationEntryType entryType,
        CancellationToken ct = default);

    /// <summary>
    /// Lấy các provisional outbound entries chưa finalize theo variant + warehouse.
    /// Dùng cho revaluation khi hàng thật đã nhập về.
    /// </summary>
    Task<List<InventoryValuationEntry>> GetOpenProvisionalOutboundEntriesAsync(
        int warehouseId,
        int productVariantId,
        CancellationToken ct = default);

    /// <summary>
    /// Lấy tất cả valuation entries của 1 order line bán gốc,
    /// bao gồm outbound và các revaluation đi theo dòng đó.
    ///
    /// Dùng cho SalesReturn mức 2 khi cần:
    /// - mirror actual outbound fragment gốc
    /// - mirror trace provisional / revaluation nếu dòng bán gốc từng provisional
    ///
    /// Không dùng average cost hiện tại.
    /// Chỉ đọc đúng lịch sử valuation của dòng bán gốc.
    /// </summary>
    Task<List<InventoryValuationEntry>> GetSaleIssueAndRevaluationEntriesByOrderLineAsync(
        int orderId,
        int orderLineId,
        CancellationToken ct = default);

    /// <summary>
    /// Lấy riêng các revaluation valuation entries của 1 order line bán gốc.
    ///
    /// Dùng khi SalesReturn cần giữ dấu vết:
    /// - dòng gốc đã từng provisional
    /// - sau đó có revaluation adjustment
    ///
    /// Từ đó service sẽ mirror đúng trace cost thay vì chỉ reverse outbound amount ban đầu.
    /// </summary>
    Task<List<InventoryValuationEntry>> GetSaleIssueRevaluationEntriesByOrderLineAsync(
        int orderId,
        int orderLineId,
        CancellationToken ct = default);

    /// <summary>
    /// Kiểm tra 1 order line bán gốc có từng phát sinh provisional valuation hay không.
    ///
    /// Mục đích:
    /// - giúp SalesReturn biết có cần giữ trace provisional
    /// - không phải tự suy luận từ average cost hay tồn hiện tại
    /// </summary>
    Task<bool> HasProvisionalValuationByOrderLineAsync(
        int orderId,
        int orderLineId,
        CancellationToken ct = default);

    /// <summary>
    /// Tính tổng quantity đã reverse từ 1 source valuation entry.
    ///
    /// Giá trị này phục vụ:
    /// - partial return
    /// - kiểm tra source fragment còn available bao nhiêu để mirror tiếp
    /// - tránh over-return cùng 1 source fragment
    ///
    /// Lưu ý:
    /// - quantity trả về là tổng quantity của các entry reverse hợp lệ
    /// - repository chỉ đọc dữ liệu; rule chọn reverse hợp lệ nằm ở query implementation
    /// </summary>
    Task<decimal> GetTotalReversedQuantityBySourceEntryIdAsync(
        int sourceValuationEntryId,
        CancellationToken ct = default);

    /// <summary>
    /// Tính tổng amount đã reverse từ 1 source valuation entry.
    ///
    /// Hữu ích cho audit và đối soát mirror valuation theo fragment.
    /// Partial return chuẩn mức 2 thường cần cả qty và amount đã reverse.
    /// </summary>
    Task<decimal> GetTotalReversedAmountBySourceEntryIdAsync(
        int sourceValuationEntryId,
        CancellationToken ct = default);
    Task<List<InventoryValuationEntry>> GetRevaluationEntriesBySourceIdAsync(
    int sourceValuationEntryId,
    CancellationToken ct = default);
}
