using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Interfaces.Repositories.POSTerminals;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Web.Middlewares;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GaoApp.Tests.Observability;

public sealed class TerminalResolutionMiddlewareTests
{
    [Fact]
    public async Task Resolution_failure_is_safe_best_effort_warning_and_request_continues()
    {
        const string secret = "synthetic-terminal-provider-secret";
        var tenant = new TenantContext();
        await using var db = CreateDbContext(tenant);
        var logger =
            new GlobalExceptionMiddlewareTests.RecordingLogger<TerminalResolutionMiddleware>();
        var nextCalls = 0;
        var middleware = new TerminalResolutionMiddleware(
            new ThrowingTerminalRepository(new IOException(secret)),
            new FakeCurrentStore(),
            db,
            logger);
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "trace-terminal"
        };
        context.Items["CurrentStoreId"] = "3";
        context.Items["CurrentStoreName"] = "Synthetic Store";
        context.Request.Headers.Cookie = "POS_DEVICE_KEY=synthetic-device";

        await middleware.InvokeAsync(
            context,
            _ =>
            {
                nextCalls++;
                return Task.CompletedTask;
            });

        Assert.Equal(1, nextCalls);
        var warning = Assert.Single(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning);
        Assert.Contains(nameof(IOException), warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_without_next_or_error_log()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var tenant = new TenantContext();
        await using var db = CreateDbContext(tenant);
        var logger =
            new GlobalExceptionMiddlewareTests.RecordingLogger<TerminalResolutionMiddleware>();
        var nextCalls = 0;
        var middleware = new TerminalResolutionMiddleware(
            new ThrowingTerminalRepository(
                new OperationCanceledException(cancellation.Token)),
            new FakeCurrentStore(),
            db,
            logger);
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "trace-terminal-cancelled",
            RequestAborted = cancellation.Token
        };
        context.Items["CurrentStoreId"] = "3";
        context.Items["CurrentStoreName"] = "Synthetic Store";
        context.Request.Headers.Cookie = "POS_DEVICE_KEY=synthetic-device";

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => middleware.InvokeAsync(
                context,
                _ =>
                {
                    nextCalls++;
                    return Task.CompletedTask;
                }));

        Assert.Equal(0, nextCalls);
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Level == LogLevel.Error);
    }

    private static AppDbContext CreateDbContext(TenantContext tenant)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"terminal-r1.6-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options, tenant, new FakeCurrentUser());
    }

    private sealed class FakeCurrentStore : ICurrentStore
    {
        public int StoreId => 3;
    }

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public int? UserId => null;
        public string? UserName => null;
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => false;
    }

    private sealed class ThrowingTerminalRepository(Exception exception)
        : IPOSTerminalRepository
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
            Task.FromException<POSTerminal?>(exception);

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
}
