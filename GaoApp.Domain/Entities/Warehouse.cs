using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Kho hàng vật lý hoặc kho logic của từng cửa hàng.
/// Ví dụ: Kho tổng, Kho bán hàng, Kho hư hỏng...
/// </summary>
public class Warehouse : BaseStoreEntity
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Location { get; set; }
    public string? Note { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Có cho phép âm kho khi bán hàng POS hay không.
    /// False = thiếu tồn thì chặn finalize.
    /// True = vẫn cho bán, tồn có thể âm và phải ghi log transaction rõ ràng.
    /// </summary>
    public bool AllowNegativeInventory { get; set; } = false;

    public ICollection<InventoryBalance> InventoryBalances { get; set; } = new List<InventoryBalance>();
    public ICollection<InventoryTransaction> InventoryTransactions { get; set; } = new List<InventoryTransaction>();

    /// <summary>
    /// Các ca POS sử dụng kho này để xuất bán.
    /// </summary>
    public ICollection<POSShift> POSShifts { get; set; } = new List<POSShift>();

    /// <summary>
    /// Các phiếu kiểm kê thuộc kho này.
    /// </summary>
    public ICollection<StockCountDocument> StockCountDocuments { get; set; } = new List<StockCountDocument>();
}