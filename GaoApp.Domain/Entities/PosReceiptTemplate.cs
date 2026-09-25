using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

public sealed class PosReceiptTemplate : BaseStoreEntity, IAuditTrackedEntity
{
    public string Name { get; set; } = "";
    public string DefinitionJson { get; set; } = "";
}
