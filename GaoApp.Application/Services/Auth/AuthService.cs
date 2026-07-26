using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Errors;
using GaoApp.Application.Common.Results;
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

    public async Task<Result<LoginResponse>> LoginAsync(
        LoginRequest request,
        CancellationToken ct = default)
    {
        var storeId = _currentStore.StoreId;
        var clientIp = _clientNetworkInfo.GetClientIp();

        // 1. Kiểm tra tài khoản trước
        if (string.IsNullOrWhiteSpace(request.UserName) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return Result<LoginResponse>.Failure(AuthErrors.InvalidCredentials);
        }

        var user = await _users.GetByUserNameAsync(request.UserName.Trim(), ct);
        if (user == null)
            return Result<LoginResponse>.Failure(AuthErrors.InvalidCredentials);

        if (!user.IsActive)
            return Result<LoginResponse>.Failure(AuthErrors.AccountInactive);

        if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
            return Result<LoginResponse>.Failure(AuthErrors.InvalidCredentials);

        var userInStore = await _users.GetUserInStoreAsync(user.Id, storeId, ct);
        if (userInStore == null)
            return Result<LoginResponse>.Failure(AuthErrors.StoreAccessDenied);

        if (!userInStore.IsActive)
            return Result<LoginResponse>.Failure(AuthErrors.AccountInactive);

        if (userInStore.Role == null || userInStore.Role.IsDeleted)
            return Result<LoginResponse>.Failure(AuthErrors.RoleUnavailable);
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
                return Result<LoginResponse>.Failure(
                    AuthErrors.TerminalSelectionRequired);
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
                return Result<LoginResponse>.Failure(AuthErrors.DevicePairingFailed);

            devicePaired = true;
        }

        return Result<LoginResponse>.Success(
            new LoginResponse
            {
                UserId = user.Id,
                UserName = user.UserName,
                FullName = user.FullName,

                RoleId = userInStore.RoleId,
                RoleCode = userInStore.Role.Code,
                RoleName = userInStore.Role.Name,

                StoreId = storeId,

                TerminalId = terminal.Id,
                TerminalCode = terminal.Code,
                TerminalName = terminal.Name,

                ClientIp = clientIp,
                DeviceKey = deviceKey,
                DevicePaired = devicePaired
            });
    }
}
