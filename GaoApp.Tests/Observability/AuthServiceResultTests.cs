using GaoApp.Application.Common.Errors;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Interfaces.Repositories.Auth;
using GaoApp.Application.Interfaces.Repositories.POSTerminals;
using GaoApp.Application.Services.Auth;
using GaoApp.Application.DTOs.Auth;
using GaoApp.Domain.Entities;

namespace GaoApp.Tests.Observability;

public sealed class AuthServiceResultTests
{
    [Fact]
    public async Task Unknown_user_is_expected_safe_result_failure()
    {
        var service = CreateService(
            new FakeUserRepository(
                (_, _) => Task.FromResult<User?>(null)));

        var result = await service.LoginAsync(ValidRequest());

        Assert.True(result.IsFailure);
        Assert.Equal(AuthErrors.InvalidCredentials.Code, result.Error.Code);
        Assert.Equal(AuthErrors.InvalidCredentials.Message, result.Error.Message);
    }

    [Fact]
    public async Task Repository_technical_failure_propagates()
    {
        var expected = new IOException("synthetic database failure");
        var service = CreateService(
            new FakeUserRepository(
                (_, _) => Task.FromException<User?>(expected)));

        var actual = await Assert.ThrowsAsync<IOException>(
            () => service.LoginAsync(ValidRequest()));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task Repository_caller_cancellation_propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var service = CreateService(
            new FakeUserRepository(
                (_, ct) => Task.FromCanceled<User?>(ct)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.LoginAsync(ValidRequest(), cts.Token));
    }

    private static AuthService CreateService(IAuthUserRepository users) =>
        new(
            users,
            new FakeTerminalRepository(),
            new FakeCurrentStore(),
            new FakeClientNetworkInfo(),
            new FakePasswordHasher());

    private static LoginRequest ValidRequest() => new()
    {
        UserName = "synthetic-user",
        Password = "synthetic-password"
    };

    private sealed class FakeUserRepository(
        Func<string, CancellationToken, Task<User?>> getUser)
        : IAuthUserRepository
    {
        public Task<User?> GetByUserNameAsync(
            string userName,
            CancellationToken ct = default) =>
            getUser(userName, ct);

        public Task<UserInStore?> GetUserInStoreAsync(
            int userId,
            int storeId,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeTerminalRepository : IPOSTerminalRepository
    {
        public Task<POSTerminal?> GetByIdAsync(
            int id,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<POSTerminal?> GetByStoreAndIpAsync(
            int storeId,
            string ip,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<List<POSTerminal>> GetActiveByStoreAsync(
            int storeId,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<POSTerminal?> GetByDeviceKeyAsync(
            int storeId,
            string deviceKey,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<POSTerminalDevice> AttachDeviceAsync(
            int storeId,
            int terminalId,
            string deviceKey,
            string? deviceName,
            string? userAgent,
            string? lastIp,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeCurrentStore : ICurrentStore
    {
        public int StoreId => 3;
    }

    private sealed class FakeClientNetworkInfo : IClientNetworkInfo
    {
        public string? GetClientIp() => "127.0.0.1";
    }

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => password;
        public bool Verify(string password, string hash) => password == hash;
    }
}
