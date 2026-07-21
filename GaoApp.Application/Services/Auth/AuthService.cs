using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Auth;
using GaoApp.Application.Interfaces.Repositories.Auth;
using GaoApp.Application.Interfaces.Repositories.POSTerminals;
using GaoApp.Application.Interfaces.Services.Auth;

namespace GaoApp.Application.Services.Auth;

public class AuthService : IAuthService
{
    private readonly IAuthUserRepository _users;
    private readonly IPOSTerminalRepository _terminals;
    private readonly ICurrentStore _currentStore;
    private readonly IClientNetworkInfo _clientNetworkInfo;
    private readonly IPasswordHasher _passwordHasher;

    public AuthService(
        IAuthUserRepository users,
        IPOSTerminalRepository terminals,
        ICurrentStore currentStore,
        IClientNetworkInfo clientNetworkInfo,
        IPasswordHasher passwordHasher)
    {
        _users = users;
        _terminals = terminals;
        _currentStore = currentStore;
        _clientNetworkInfo = clientNetworkInfo;
        _passwordHasher = passwordHasher;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var storeId = _currentStore.StoreId;
        var clientIp = _clientNetworkInfo.GetClientIp();

        // 1. Kiểm tra tài khoản trước
        var user = await _users.GetByUserNameAsync(request.UserName.Trim(), ct)
            ?? throw new InvalidOperationException("Tài khoản hoặc mật khẩu không đúng.");

        if (!user.IsActive)
            throw new InvalidOperationException("Tài khoản đã bị khóa.");

        if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
            throw new InvalidOperationException("Tài khoản hoặc mật khẩu không đúng.");

        var userInStore = await _users.GetUserInStoreAsync(user.Id, storeId, ct);
        if (userInStore == null)
            throw new InvalidOperationException("Tài khoản không có quyền truy cập cửa hàng hiện tại.");

        if (!userInStore.IsActive)
            throw new InvalidOperationException("Tài khoản đã bị khóa tại cửa hàng hiện tại.");
        if (userInStore.Role == null || userInStore.Role.IsDeleted)
            throw new InvalidOperationException("Vai trò của tài khoản không còn hợp lệ.");
        // 2. Resolve terminal bằng DeviceKey
        var deviceKey = request.DeviceKey;
        var devicePaired = false;

        GaoApp.Domain.Entities.POSTerminal? terminal = null;

        if (!string.IsNullOrWhiteSpace(deviceKey))
        {
            terminal = await _terminals.GetByDeviceKeyAsync(storeId, deviceKey, ct);
        }

        // 3. Nếu chưa có terminal từ cookie thì bắt buộc chọn terminal để ghép
        if (terminal == null)
        {
            if (!request.SelectedTerminalId.HasValue || request.SelectedTerminalId.Value <= 0)
            {
                throw new InvalidOperationException(
                    "Thiết bị này chưa được ghép POS. Vui lòng chọn máy POS để tiếp tục.");
            }

            deviceKey = Guid.NewGuid().ToString("N");

            await _terminals.AttachDeviceAsync(
                storeId: storeId,
                terminalId: request.SelectedTerminalId.Value,
                deviceKey: deviceKey,
                deviceName: request.DeviceName,
               userAgent: request.UserAgent,
                lastIp: clientIp,
                ct: ct);

            terminal = await _terminals.GetByDeviceKeyAsync(storeId, deviceKey, ct);

            if (terminal == null)
                throw new InvalidOperationException("Ghép thiết bị POS không thành công. Vui lòng thử lại.");

            devicePaired = true;
        }

        return new LoginResponse
        {
            UserId = user.Id,
            UserName = user.UserName,
            FullName = user.FullName,

            RoleId = userInStore.RoleId,
            RoleCode = userInStore.Role?.Code ?? string.Empty,
            RoleName = userInStore.Role?.Name ?? string.Empty,

            StoreId = storeId,

            TerminalId = terminal.Id,
            TerminalCode = terminal.Code,
            TerminalName = terminal.Name,

            ClientIp = clientIp,
            DeviceKey = deviceKey,
            DevicePaired = devicePaired
        };
    }
}