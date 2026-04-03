using GaoApp.Domain.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace GaoApp.Domain.Entities;

[Table("Taxes")]
public class Tax : BaseLookupStoreEntity
{
    [Column(TypeName = "decimal(5,2)")]
    public decimal Rate { get; set; } // 0..100
}
