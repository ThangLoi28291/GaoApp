using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Entities;

namespace GaoApp.Domain.Common;

public abstract class BaseStoreEntity : BaseEntity
{
    public int StoreId { get; set; }

    [ForeignKey(nameof(StoreId))]
    public Store? Store { get; set; }
}
