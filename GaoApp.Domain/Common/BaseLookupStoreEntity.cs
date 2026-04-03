using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GaoApp.Domain.Common;

public abstract class BaseLookupStoreEntity : BaseStoreEntity
{
    [Required, StringLength(30)]
    public string Code { get; set; } = default!;

    [Required, StringLength(200)]
    public string Name { get; set; } = default!;

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; } = 0;
}
