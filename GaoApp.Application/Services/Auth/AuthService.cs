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
        if (string.IsNullOrWhiteSpace(clientIp))
            throw new InvalidOperationException("Không xác định được IP máy hiện tại.");

        var terminal = await _terminals.GetByStoreAndIpAsync(storeId, clientIp, ct);
        if (terminal == null)
            throw new InvalidOperationException(
                $"Máy hiện tại chưa được khai báo POS terminal cho store này. IP hiện tại: {clientIp}");

        var user = await _users.GetByUserNameAsync(request.UserName.Trim(), ct)
            ?? throw new InvalidOperationException("Tài khoản hoặc mật khẩu không đúng.");

        if (!user.IsActive)
            throw new InvalidOperationException("Tài khoản đã bị khóa.");

        if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
            throw new InvalidOperationException("Tài khoản hoặc mật khẩu không đúng.");

        var userInStore = await _users.GetUserInStoreAsync(user.Id, storeId, ct);
        if (userInStore == null)
            throw new InvalidOperationException("Tài khoản không có quyền truy cập cửa hàng hiện tại.");

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
            ClientIp = clientIp
        };
    }
}