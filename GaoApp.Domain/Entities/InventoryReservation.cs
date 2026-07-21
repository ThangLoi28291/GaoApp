using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

[Table("InventoryReservations")]
public class InventoryReservation : BaseStoreEntity
{
    public int WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public int ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = null!;

    /// <summary>
    /// Nguồn giữ hàng, phase hiện tại chủ yếu là Order (POS OnHold).
    /// </summary>
    public InventoryReferenceType ReferenceType { get; set; }

    /// <summary>
    /// Id chứng từ nguồn, ví dụ OrderId.
    /// Lưu string để đồng bộ với InventoryTransaction.
    /// </summary>
    [Required]
    [StringLength(50)]
    public string ReferenceId { get; set; } = default!;

    /// <summary>
    /// Id dòng chứng từ nguồn, ví dụ OrderLineId.
    /// Reserve theo line sẽ rõ ràng hơn cho việc release/consume sau này.
    /// </summary>
    public int? ReferenceLineId { get; set; }

    /// <summary>
    /// Số lượng giữ theo đơn vị gốc.
    /// </summary>
    public decimal ReservedQty { get; set; }

    /// <summary>
    /// Trạng thái reservation.
    /// </summary>
    public InventoryReservationStatus Status { get; set; } = InventoryReservationStatus.Active;

    [StringLength(500)]
    public string? Note { get; set; }

    public DateTime ReservedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? ReleasedAtUtc { get; set; }

    [StringLength(500)]
    public string? ReleaseNote { get; set; }
}