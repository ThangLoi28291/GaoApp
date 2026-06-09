using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Sổ phát sinh tích điểm của khách hàng.
/// 
/// Lưu ý:
/// - Không lưu "điểm" trực tiếp.
/// - Amount là "số tiền tích lũy hợp lệ".
/// - Điểm = Amount / RewardSettings.MoneyPerPoint.
/// </summary>
[Table("CustomerRewardLedgers")]
public class CustomerRewardLedger : BaseStoreEntity
{
    /// <summary>
    /// Khách hàng phát sinh tích điểm.
    /// </summary>
    public int CustomerId { get; set; }

    [ForeignKey(nameof(CustomerId))]
    public Customer? Customer { get; set; }

    /// <summary>
    /// Loại phát sinh: bán hàng, trả hàng, import, đổi phiếu...
    /// </summary>
    public CustomerRewardLedgerType Type { get; set; }

    /// <summary>
    /// Số tiền tích lũy hợp lệ.
    /// 
    /// Quy ước:
    /// - Dương: cộng tích lũy.
    /// - Âm: trừ tích lũy.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    /// <summary>
    /// Id đơn bán hàng nếu phát sinh từ đơn bán.
    /// </summary>
    public int? OrderId { get; set; }

    [ForeignKey(nameof(OrderId))]
    public Order? Order { get; set; }

    /// <summary>
    /// Id phiếu trả hàng nếu phát sinh từ trả hàng.
    /// </summary>
    public int? SalesReturnId { get; set; }

    [ForeignKey(nameof(SalesReturnId))]
    public SalesReturn? SalesReturn { get; set; }

    /// <summary>
    /// Id voucher nếu phát sinh từ đổi/dùng phiếu.
    /// Giai đoạn sau tạo CustomerRewardVoucher rồi sẽ gắn vào.
    /// </summary>
    public int? VoucherId { get; set; }

    [ForeignKey(nameof(VoucherId))]
    public CustomerRewardVoucher? Voucher { get; set; }

    /// <summary>
    /// Mã tham chiếu ngoài, dùng khi import dữ liệu cũ hoặc đối soát.
    /// Ví dụ: OldUserPointId, OldOrderId.
    /// </summary>
    [StringLength(100)]
    public string? ReferenceCode { get; set; }

    /// <summary>
    /// Nội dung hiển thị cho người dùng/admin.
    /// </summary>
    [StringLength(500)]
    public string? Description { get; set; }

}