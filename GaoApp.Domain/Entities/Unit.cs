using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

[Table("Unit")]
public class Unit : BaseStoreEntity
{
    [Required, StringLength(30)]
    public string Code { get; set; } = default!;

    [Required, StringLength(200)]
    public string Name { get; set; } = default!;

    public bool IsActive { get; set; } = true;

    public bool IsBase { get; set; } = false;

    public int SortOrder { get; set; } = 0;

    /// <summary>
    /// Các dòng quy đổi đơn vị đang dùng unit này.
    /// </summary>
    public ICollection<ProductUnitConversion> ProductUnitConversions { get; set; }
        = new List<ProductUnitConversion>();

    /// <summary>
    /// Các dòng kiểm kê dùng đơn vị này.
    /// </summary>
    public ICollection<StockCountLine> StockCountLines { get; set; } = new List<StockCountLine>();
}