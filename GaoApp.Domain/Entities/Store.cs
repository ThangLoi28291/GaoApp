using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

public class Store : BaseEntity
{
    public string Name { get; set; } = default!;

    public string SubDomain { get; set; } = default!;
    public string SubDomainNormalized { get; set; } = default!;

    public bool IsActive { get; set; } = true;
}
