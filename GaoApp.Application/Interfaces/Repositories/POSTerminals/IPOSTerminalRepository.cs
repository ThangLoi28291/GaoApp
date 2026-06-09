using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.POSTerminals;

public interface IPOSTerminalRepository
{
    Task<POSTerminal?> GetByIdAsync(int id, CancellationToken ct = default);

    // Giữ lại tạm để code cũ chưa vỡ, nhưng từ bước sau sẽ không dùng để resolve POS nữa.
    Task<POSTerminal?> GetByStoreAndIpAsync(int storeId, string ip, CancellationToken ct = default);

    Task<List<POSTerminal>> GetActiveByStoreAsync(int storeId, CancellationToken ct = default);

    // Mới: tìm terminal bằng DeviceKey trong cookie.
    Task<POSTerminal?> GetByDeviceKeyAsync(int storeId, string deviceKey, CancellationToken ct = default);

    // Mới: ghép thiết bị hiện tại vào terminal.
    Task<POSTerminalDevice> AttachDeviceAsync(
        int storeId,
        int terminalId,
        string deviceKey,
        string? deviceName,
        string? userAgent,
        string? lastIp,
        CancellationToken ct = default);
}