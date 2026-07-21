using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

[Table("OrderInventoryIssueActions")]
public class OrderInventoryIssueAction : BaseStoreEntity
{
    [Range(1, int.MaxValue)]
    public int OrderInventoryIssueId { get; set; }
    public OrderInventoryIssue OrderInventoryIssue { get; set; } = default!;

    /// <summary>
    /// Mức 2:
    /// Cho phép action gắn trực tiếp tới 1 issue line để resolve chính xác theo line.
    /// Nullable để không phá dữ liệu/action cũ ở mức header.
    /// </summary>
    public int? OrderInventoryIssueLineId { get; set; }
    public OrderInventoryIssueLine? OrderInventoryIssueLine { get; set; }

    public InventoryIssueActionType ActionType { get; set; }

    public int? ActorUserId { get; set; }
    /// <summary>
    /// User thực hiện action.
    /// Nullable để hỗ trợ action sinh tự động từ hệ thống.
    /// </summary>
    public User? ActorUser { get; set; }

    public DateTime ActionAtUtc { get; set; }

    public InventoryIssueReferenceType ReferenceType { get; set; }

    public int? ReferenceId { get; set; }

    [StringLength(1000)]
    public string? Note { get; set; }
}