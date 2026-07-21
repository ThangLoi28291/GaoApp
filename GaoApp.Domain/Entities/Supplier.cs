using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

[Table("Suppliers")]
public class Supplier : BaseLookupStoreEntity
{
    [StringLength(30)]
    public string? Phone { get; set; }

    [StringLength(200)]
    public string? Email { get; set; }

    [StringLength(300)]
    public string? Address { get; set; }

    [StringLength(150)]
    public string? ContactName { get; set; }

    [StringLength(50)]
    public string? TaxCode { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
}
