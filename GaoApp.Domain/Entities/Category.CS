using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

[Table("Category")]
public class Category : BaseLookupStoreEntity, IAuditTrackedEntity
{
    // Nếu sau này cần Category cha/con:
    public int? ParentId { get; set; }
    public Category? Parent { get; set; }
    /// <summary>
    /// Ngành hàng này có được tích điểm hay không.
    /// Ví dụ: Sữa = false.
    /// </summary>
    public bool IsRewardEligible { get; set; } = true;
    public ICollection<Category> Children { get; set; } = new List<Category>();
}
