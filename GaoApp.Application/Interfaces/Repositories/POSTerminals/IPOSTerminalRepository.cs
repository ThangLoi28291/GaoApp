using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.POSTerminals;

public interface IPOSTerminalRepository
{
    Task<POSTerminal?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<POSTerminal?> GetByStoreAndIpAsync(int storeId, string ip, CancellationToken ct = default);
    Task<List<POSTerminal>> GetActiveByStoreAsync(int storeId, CancellationToken ct = default);
}