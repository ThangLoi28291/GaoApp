using System.ComponentModel.DataAnnotations;

namespace GaoApp.Domain.Common;

public abstract class BaseEntity
{
    public int Id { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }
    public int? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public int? DeletedBy { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = default!;
}
    