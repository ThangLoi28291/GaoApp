using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

[Table("OrderNumberSequences")]
public class OrderNumberSequence : BaseStoreEntity
{
    [StringLength(8)]
    public string DateKey { get; set; } = default!; // yyyyMMdd

    public int LastNumber { get; set; }
}