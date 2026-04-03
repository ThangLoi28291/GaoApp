using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

[Table("SalesReturnLines")]
public class SalesReturnLine : BaseStoreEntity
{
    public int SalesReturnId { get; set; }
    public SalesReturn SalesReturn { get; set; } = default!;

    /// <summary>
    /// Dòng bán gốc liên quan.
    /// </summary>
    public int OrderLineId { get; set; }
    public OrderLine OrderLine { get; set; } = default!;

    public int ProductId { get; set; }
    public int VariantId { get; set; }

    [Required]
    [StringLength(250)]
    public string ItemName { get; set; } = default!;

    [StringLength(100)]
    public string? UnitName { get; set; }

    /// <summary>
    /// Số lượng trả theo đơn vị bán.
    /// </summary>
    public decimal ReturnQuantity { get; set; }

    /// <summary>
    /// Số lượng quy đổi về đơn vị gốc để nhập kho.
    /// </summary>
    public decimal ReturnBaseQuantity { get; set; }

    /// <summary>
    /// Số tiền hoàn trên 1 đơn vị bán.
    /// </summary>
    public decimal RefundUnitAmount { get; set; }

    /// <summary>
    /// Tổng tiền hoàn của dòng.
    /// </summary>
    public decimal RefundLineTotal { get; set; }

    public SalesReturnLineAction Action { get; set; }

    [StringLength(500)]
    public string? Reason { get; set; }

    /// <summary>
    /// Đơn giá vốn snapshot dùng cho dòng trả hàng.
    /// 
    /// Thông thường ưu tiên lấy từ OrderLine gốc.
    /// Nếu không truy được dòng gốc thì mới fallback theo policy.
    /// </summary>
    public decimal UnitCostSnapshot { get; set; }

    /// <summary>
    /// Tổng giá vốn của dòng trả hàng.
    /// 
    /// Với hàng trả nhập lại kho, field này giúp hạch toán đảo COGS rõ ràng.
    /// </summary>
    public decimal LineCostTotal { get; set; }

    /// <summary>
    /// Dòng trả hàng này có đang dùng cost tạm hay không.
    /// </summary>
    public bool IsProvisionalCost { get; set; }
}