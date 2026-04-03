using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

[Table("Brands")]
public class Brand : BaseLookupStoreEntity, IAuditTrackedEntity
{
    [StringLength(300)]
    public string? Description { get; set; }
}
