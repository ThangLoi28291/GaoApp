using GaoApp.Domain.Common;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Số dư tồn kho hiện tại của 1 ProductVariant trong 1 Warehouse.
/// 
/// Đây là bảng snapshot để đọc nhanh tồn hiện tại, được cập nhật sau mỗi lần
/// phát sinh InventoryTransaction hợp lệ.
/// 
/// Lưu ý kiến trúc:
/// - InventoryBalance là trạng thái hiện tại (current state).
/// - InventoryTransaction là ledger/sổ cái biến động.
/// - Khi cần audit hoặc truy vết, luôn ưu tiên đọc InventoryTransaction.
/// </summary>
public class InventoryBalance : BaseStoreEntity
{
    /// <summary>
    /// Kho chứa hàng.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int WarehouseId { get; set; }



    /// <summary>
    /// Biến thể sản phẩm.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int ProductVariantId { get; set; }

    /// <summary>
    /// Tồn thực tế hiện có trong kho theo sổ hệ thống.
    /// 
    /// Giá trị này được cộng/trừ trực tiếp từ các InventoryTransaction đã ghi.
    /// Có thể âm nếu kho/store bật chính sách cho phép âm kho.
    /// </summary>
    public decimal OnHandQty { get; set; }

    /// <summary>
    /// Số lượng đang được reserve / giữ chỗ.
    /// 
    /// Ví dụ về tương lai:
    /// - đơn nháp giữ hàng
    /// - đơn bán đã allocate nhưng chưa xuất
    /// - pick/pack chưa hoàn tất
    /// - reservation cho kênh bán hàng
    /// 
    /// Phase hiện tại:
    /// - Chưa activate đầy đủ luồng reservation thực tế.
    /// - Thuộc tính này được giữ sẵn để chuẩn bị cho Phase 6.
    /// - Vì vậy trong nhiều nghiệp vụ hiện tại ReservedQty có thể vẫn = 0.
    /// </summary>
    public decimal ReservedQty { get; set; }

    /// <summary>
    /// Số lượng khả dụng để bán / xuất theo logic reservation.
    /// 
    /// Công thức chuẩn:
    /// AvailableQty = OnHandQty - ReservedQty
    /// 
    /// Ý nghĩa:
    /// - OnHandQty: tồn đang có theo hệ thống
    /// - ReservedQty: phần đã bị giữ chỗ, chưa được dùng tiếp
    /// - AvailableQty: phần thực sự còn khả dụng cho nghiệp vụ mới
    /// 
    /// Lưu ý:
    /// Phase hiện tại chưa triển khai reservation flow đầy đủ,
    /// nên AvailableQty hiện gần như sẽ bằng OnHandQty trong đa số trường hợp.
    /// </summary>
    [NotMapped]
    public decimal AvailableQty => OnHandQty - ReservedQty;

    // =========================
    // Navigation properties
    // =========================

    /// <summary>
    /// Tổng giá trị tồn kho hiện tại của balance này.
    /// 
    /// Công thức ý nghĩa:
    /// - Với tồn dương ổn định: gần đúng = OnHandQty * AverageUnitCost
    /// - Với các case revaluation/phát sinh làm tròn: đây là source of truth
    /// </summary>
    public decimal InventoryValue { get; set; }

    /// <summary>
    /// Đơn giá vốn bình quân đang có hiệu lực cho balance này.
    /// 
    /// Đây là field rất quan trọng để:
    /// - bán hàng bình thường lấy cost nhanh
    /// - hỗ trợ moving average
    /// - làm base cost khi không bị provisional
    /// </summary>
    
    public decimal AverageUnitCost { get; set; }

    /// <summary>
    /// Đơn giá nhập gần nhất.
    /// 
    /// Field này rất hữu ích làm fallback provisional cost
    /// khi moving average chưa đáng tin do âm kho hoặc chưa có tồn dương.
    /// </summary>
    public decimal? LastInboundUnitCost { get; set; }

    /// <summary>
    /// Thời điểm nhập gần nhất.
    /// Dùng để audit/fallback/debug thứ tự cost source.
    /// </summary>
    public DateTime? LastInboundAtUtc { get; set; }

    /// <summary>
    /// Lần cuối balance này được valuation/revaluation cập nhật.
    /// </summary>
    public DateTime? LastValuationAtUtc { get; set; }

    public Warehouse Warehouse { get; set; } = null!;

    public ProductVariant ProductVariant { get; set; } = null!;
}