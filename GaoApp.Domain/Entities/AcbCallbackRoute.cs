using System.ComponentModel.DataAnnotations;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

// Global routing configuration: it must be available before tenant resolution.
public sealed class AcbCallbackRoute : BaseEntity
{
    [MaxLength(253)] public string Host { get; set; } = "";
    public int? TargetStoreId { get; set; }
}

public sealed class AcbCallbackRouteChange
{
    public long Id { get; set; }
    public int RouteId { get; set; }
    public AcbCallbackRoute Route { get; set; } = null!;
    public int? PreviousStoreId { get; set; }
    public int? TargetStoreId { get; set; }
    public int ActorUserId { get; set; }
    public DateTime ChangedAtUtc { get; set; }
}
