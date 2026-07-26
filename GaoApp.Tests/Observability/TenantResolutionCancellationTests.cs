using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Options;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Web.Middlewares;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.Observability;

public sealed class TenantResolutionCancellationTests
{
    [Fact]
    public async Task Localhost_development_cancellation_propagates_without_next_or_error_log()
    {
        var tenant = new TenantContext();
        await using var db = CreateDbContext(tenant);
        var nextCalls = 0;
        var logger =
            new GlobalExceptionMiddlewareTests.RecordingLogger<TenantResolutionMiddleware>();
        var middleware = CreateMiddleware(
            _ =>
            {
                nextCalls++;
                return Task.CompletedTask;
            },
            logger,
            Environments.Development);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var context = CreateContext("localhost", "?tenant=shop", cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => middleware.InvokeAsync(context, db, tenant));

        Assert.Equal(0, nextCalls);
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Normal_subdomain_cancellation_propagates_without_next_or_error_log()
    {
        var tenant = new TenantContext();
        await using var db = CreateDbContext(tenant);
        var nextCalls = 0;
        var logger =
            new GlobalExceptionMiddlewareTests.RecordingLogger<TenantResolutionMiddleware>();
        var middleware = CreateMiddleware(
            _ =>
            {
                nextCalls++;
                return Task.CompletedTask;
            },
            logger,
            Environments.Production);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var context = CreateContext(
            "shop.example.test",
            queryString: null,
            cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => middleware.InvokeAsync(context, db, tenant));

        Assert.Equal(0, nextCalls);
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Localhost_success_resolves_store_and_calls_next()
    {
        var tenant = new TenantContext();
        await using var db = CreateDbContext(tenant);
        var store = new Store
        {
            Name = "Synthetic Store",
            SubDomain = "shop",
            SubDomainNormalized = "shop",
            IsActive = true,
            RowVersion = new byte[8]
        };
        db.Stores.Add(store);
        await db.SaveChangesAsync();
        tenant.Clear();
        var nextCalls = 0;
        var middleware = CreateMiddleware(
            _ =>
            {
                nextCalls++;
                return Task.CompletedTask;
            },
            new GlobalExceptionMiddlewareTests.RecordingLogger<TenantResolutionMiddleware>(),
            Environments.Development);
        var context = CreateContext("localhost", "?tenant=shop");

        await middleware.InvokeAsync(context, db, tenant);

        Assert.Equal(1, nextCalls);
        Assert.Equal(store.Id, tenant.StoreId);
        Assert.Equal("shop", tenant.Subdomain);
    }

    [Fact]
    public async Task Localhost_not_found_keeps_existing_continue_behavior()
    {
        var tenant = new TenantContext();
        await using var db = CreateDbContext(tenant);
        var nextCalls = 0;
        var middleware = CreateMiddleware(
            _ =>
            {
                nextCalls++;
                return Task.CompletedTask;
            },
            new GlobalExceptionMiddlewareTests.RecordingLogger<TenantResolutionMiddleware>(),
            Environments.Development);
        var context = CreateContext("localhost", "?tenant=missing");

        await middleware.InvokeAsync(context, db, tenant);

        Assert.Equal(1, nextCalls);
        Assert.Null(tenant.StoreId);
    }

    [Fact]
    public async Task Technical_database_failure_propagates_to_global_owner_as_generic_500_once()
    {
        var tenant = new TenantContext();
        var db = CreateDbContext(tenant);
        await db.DisposeAsync();
        var nextCalls = 0;
        var tenantLogger =
            new GlobalExceptionMiddlewareTests.RecordingLogger<TenantResolutionMiddleware>();
        var tenantMiddleware = CreateMiddleware(
            _ =>
            {
                nextCalls++;
                return Task.CompletedTask;
            },
            tenantLogger,
            Environments.Development);
        var context = CreateContext("localhost", "?tenant=shop");
        context.TraceIdentifier = "trace-tenant-technical";
        context.Response.Body = new MemoryStream();
        var globalLogger =
            new GlobalExceptionMiddlewareTests.RecordingLogger<GlobalExceptionMiddleware>();
        var globalMiddleware = new GlobalExceptionMiddleware(
            requestContext => tenantMiddleware.InvokeAsync(
                requestContext,
                db,
                tenant),
            globalLogger);

        await globalMiddleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(
            context.Response.Body,
            leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        Assert.Equal(
            StatusCodes.Status500InternalServerError,
            context.Response.StatusCode);
        Assert.Equal(0, nextCalls);
        Assert.DoesNotContain(
            tenantLogger.Entries,
            entry => entry.Level == LogLevel.Error);
        Assert.Single(
            globalLogger.Entries,
            entry => entry.Level == LogLevel.Error);
        Assert.DoesNotContain(
            nameof(ObjectDisposedException),
            body,
            StringComparison.Ordinal);
    }

    private static TenantResolutionMiddleware CreateMiddleware(
        RequestDelegate next,
        ILogger<TenantResolutionMiddleware> logger,
        string environment) =>
        new(
            next,
            logger,
            Options.Create(new TenantOptions
            {
                RootDomain = "example.test",
                AdminSubdomain = "admin"
            }),
            new FakeWebHostEnvironment
            {
                EnvironmentName = environment
            });

    private static AppDbContext CreateDbContext(TenantContext tenant)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"tenant-r1.6-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options, tenant, new FakeCurrentUser());
    }

    private static DefaultHttpContext CreateContext(
        string host,
        string? queryString,
        CancellationToken cancellationToken = default)
    {
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "trace-tenant",
            RequestAborted = cancellationToken
        };
        context.Request.Host = new HostString(host);
        context.Request.Scheme = "http";
        context.Request.Path = "/admin";
        if (!string.IsNullOrWhiteSpace(queryString))
            context.Request.QueryString = new QueryString(queryString);

        return context;
    }

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public int? UserId => null;
        public string? UserName => null;
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => false;
    }

    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "GaoApp.Tests";
        public string WebRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider WebRootFileProvider { get; set; } =
            new NullFileProvider();
        public string ContentRootPath { get; set; } =
            Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "GaoApp.Tests";
        public string ContentRootPath { get; set; } =
            Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
