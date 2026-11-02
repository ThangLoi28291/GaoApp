using GaoApp.Application.DTOs.Audit;
using GaoApp.Application.Interfaces.Services.Audit;

namespace GaoApp.Infrastructure.Data.Seed;

/// <summary>Explicit audit ownership for the Migrator's first-store bootstrap.</summary>
public sealed class ProvisioningAuditContext : IAuditExecutionContextAccessor
{
    private StoreScope? current;

    public AuditExecutionContextDto GetCurrent() => new()
    {
        StoreId = current?.StoreId,
        UserName = "SYSTEM:migrator"
    };

    public IDisposable BeginStore(int storeId)
    {
        if (storeId <= 0) throw new ArgumentOutOfRangeException(nameof(storeId));
        if (current is not null) throw new InvalidOperationException("A provisioning audit store is already active.");
        return current = new StoreScope(this, storeId);
    }

    private sealed class StoreScope(ProvisioningAuditContext owner, int storeId) : IDisposable
    {
        public int StoreId { get; } = storeId;
        private bool disposed;
        public void Dispose()
        {
            if (disposed) return;
            if (!ReferenceEquals(owner.current, this)) throw new InvalidOperationException("Invalid provisioning audit scope.");
            owner.current = null;
            disposed = true;
        }
    }
}
