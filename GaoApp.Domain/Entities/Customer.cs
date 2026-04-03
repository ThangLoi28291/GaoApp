using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

[Table("Customers")]
public class Customer : BaseStoreEntity
{
    [Required, StringLength(200)]
    public string Name { get; set; } = default!;

    [StringLength(30)]
    public string? Phone { get; set; }

    [StringLength(300)]
    public string? Address { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }

    public bool IsActive { get; set; } = true;
}