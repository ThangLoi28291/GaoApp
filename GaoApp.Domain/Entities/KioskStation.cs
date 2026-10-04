using System.ComponentModel.DataAnnotations;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

/// <summary>A dedicated, revocable self-service terminal. Never an employee login.</summary>
public sealed class KioskStation : BaseStoreEntity
{
    public int TerminalId { get; set; }
    public POSTerminal Terminal { get; set; } = default!;
    public int WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = default!;
    public int SystemUserId { get; set; }
    public User SystemUser { get; set; } = default!;
    public bool IsActive { get; set; } = true;
    public bool IsPaused { get; set; }
    [MaxLength(64)] public string? ActivationHash { get; set; }
    public DateTime? ActivationExpiresAtUtc { get; set; }
    [MaxLength(64)] public string? DeviceHash { get; set; }
    public DateTime? LastSeenAtUtc { get; set; }
    public DateTime? HelpRequestedAtUtc { get; set; }
    public int? OrderId { get; set; }
    public Guid SessionKey { get; set; } = Guid.NewGuid();
    public long Revision { get; set; }
    public Guid? LastCommandId { get; set; }
    [MaxLength(64)] public string? LastCommandHash { get; set; }
    public Guid? CheckoutKey { get; set; }
    public DateTime? CartTouchedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int? CustomerId { get; set; }
    public DateTime? CustomerExpiresAtUtc { get; set; }
}
