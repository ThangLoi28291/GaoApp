using GaoApp.Application.Interfaces.Repositories.POSTerminals;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.POSTerminals;

public class POSTerminalRepository : IPOSTerminalRepository
{
    private readonly AppDbContext _db;

    public POSTerminalRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<POSTerminal?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _db.POSTerminals
            .FirstOrDefaultAsync(x =>
                x.Id == id &&
                !x.IsDeleted,
                ct);
    }

    /// <summary>
    /// Hàm cũ: giữ lại để không vỡ code.
    /// Từ phase DeviceKey sẽ không dùng hàm này để nhận diện terminal nữa.
    /// </summary>
    public async Task<POSTerminal?> GetByStoreAndIpAsync(int storeId, string ip, CancellationToken ct = default)
    {
        return await _db.POSTerminals
            .FirstOrDefaultAsync(x =>
                x.StoreId == storeId &&
                x.LocalIp == ip &&
                x.IsActive &&
                x.Status == POSTerminalStatus.Active &&
                !x.IsDeleted,
                ct);
    }

    public async Task<List<POSTerminal>> GetActiveByStoreAsync(int storeId, CancellationToken ct = default)
    {
        return await _db.POSTerminals
            .AsNoTracking()
            .Where(x =>
                x.StoreId == storeId &&
                x.IsActive &&
                x.Status == POSTerminalStatus.Active &&
                !x.IsDeleted)
            .OrderBy(x => x.Code)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Tìm terminal bằng DeviceKey lưu trong cookie POS_DEVICE_KEY.
    /// Đây là cách nhận diện POS mới, thay cho LocalIp.
    /// </summary>
    public async Task<POSTerminal?> GetByDeviceKeyAsync(
        int storeId,
        string deviceKey,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(deviceKey))
            return null;

        return await _db.POSTerminalDevices
            .AsNoTracking()
            .Where(d =>
                d.StoreId == storeId &&
                d.DeviceKey == deviceKey &&
                d.IsActive &&
                !d.IsDeleted &&
                d.Terminal.IsActive &&
                d.Terminal.Status == POSTerminalStatus.Active &&
                !d.Terminal.IsDeleted)
            .Select(d => d.Terminal)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Ghép thiết bị hiện tại vào một terminal.
    /// Nếu DeviceKey đã tồn tại thì cập nhật lại thông tin.
    /// Nếu chưa có thì tạo mới.
    /// </summary>
    public async Task<POSTerminalDevice> AttachDeviceAsync(
        int storeId,
        int terminalId,
        string deviceKey,
        string? deviceName,
        string? userAgent,
        string? lastIp,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(deviceKey))
            throw new ArgumentException("DeviceKey không được rỗng.", nameof(deviceKey));

        var terminal = await _db.POSTerminals
            .FirstOrDefaultAsync(x =>
                x.Id == terminalId &&
                x.StoreId == storeId &&
                x.IsActive &&
                x.Status == POSTerminalStatus.Active &&
                !x.IsDeleted,
                ct);

        if (terminal == null)
            throw new InvalidOperationException("Terminal không tồn tại hoặc đã ngừng hoạt động.");

        var device = await _db.POSTerminalDevices
            .FirstOrDefaultAsync(x =>
                x.StoreId == storeId &&
                x.DeviceKey == deviceKey &&
                !x.IsDeleted,
                ct);

        if (device == null)
        {
            device = new POSTerminalDevice
            {
                StoreId = storeId,
                TerminalId = terminalId,
                DeviceKey = deviceKey,
                DeviceName = deviceName,
                UserAgent = userAgent,
                LastIp = lastIp,
                LastSeenAtUtc = DateTime.UtcNow,
                IsActive = true
            };

            await _db.POSTerminalDevices.AddAsync(device, ct);
        }
        else
        {
            device.TerminalId = terminalId;
            device.DeviceName = deviceName;
            device.UserAgent = userAgent;
            device.LastIp = lastIp;
            device.LastSeenAtUtc = DateTime.UtcNow;
            device.IsActive = true;
        }

        await _db.SaveChangesAsync(ct);

        return device;
    }
}