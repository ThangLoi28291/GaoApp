using System.Security.Claims;
using System.Text.Json;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Errors;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Audit;
using GaoApp.Application.DTOs.Auth;
using GaoApp.Application.Interfaces.Repositories.POSTerminals;
using GaoApp.Application.Interfaces.Services.Audit;
using GaoApp.Application.Interfaces.Services.Auth;
using GaoApp.Domain.Entities;
using GaoApp.Web.Areas.Admin.ViewModels.Account;
using GaoApp.Web.Controllers;
using GaoApp.Web.Middlewares;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GaoApp.Tests.Observability;

public sealed class AccountControllerObservabilityTests
{
    [Fact]
    public async Task Invalid_credentials_return_safe_login_view_without_sign_in()
    {
        var auth = FakeAuthService.Returning(
            Result<LoginResponse>.Failure(AuthErrors.InvalidCredentials));
        var authentication = new RecordingAuthenticationService();
        var audit = new RecordingAuditLogService();
        var logger =
            new GlobalExceptionMiddlewareTests.RecordingLogger<AccountController>();
        var controller = CreateController(
            auth,
            authentication,
            audit,
            logger);

        var result = await controller.Login(ValidViewModel(), default);

        Assert.IsType<ViewResult>(result);
        Assert.Equal(0, authentication.SignInCalls);
        Assert.Equal(1, audit.WriteCalls);
        var error = Assert.Single(
            controller.ModelState[string.Empty]!.Errors);
        Assert.Equal(AuthErrors.InvalidCredentials.Message, error.ErrorMessage);
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Successful_login_signs_in_and_redirects()
    {
        var authentication = new RecordingAuthenticationService();
        var audit = new RecordingAuditLogService();
        var controller = CreateController(
            FakeAuthService.Returning(
                Result<LoginResponse>.Success(SuccessfulLogin())),
            authentication,
            audit);

        var result = await controller.Login(ValidViewModel(), default);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/admin", redirect.Url);
        Assert.Equal(1, authentication.SignInCalls);
        Assert.Equal(1, audit.WriteCalls);
        Assert.True(audit.LastRequest!.IsSuccess);
    }

    [Fact]
    public async Task Sign_in_invalid_operation_propagates_without_model_error()
    {
        var expected = new InvalidOperationException(
            "Authentication handler configuration failed: synthetic-detail");
        var authentication = new RecordingAuthenticationService
        {
            SignInException = expected
        };
        var audit = new RecordingAuditLogService();
        var controller = CreateController(
            FakeAuthService.Returning(
                Result<LoginResponse>.Success(SuccessfulLogin())),
            authentication,
            audit);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.Login(ValidViewModel(), default));

        Assert.Same(expected, actual);
        Assert.True(controller.ModelState.IsValid);
        Assert.Equal(0, audit.WriteCalls);
    }

    [Fact]
    public async Task Sign_in_failure_through_global_owner_is_generic_500_once()
    {
        var authentication = new RecordingAuthenticationService
        {
            SignInException = new InvalidOperationException(
                "Authentication handler configuration failed: synthetic-detail")
        };
        var controller = CreateController(
            FakeAuthService.Returning(
                Result<LoginResponse>.Success(SuccessfulLogin())),
            authentication,
            new RecordingAuditLogService());
        var context = controller.HttpContext;
        context.TraceIdentifier = "trace-login-technical";
        context.Response.Body = new MemoryStream();
        var logger =
            new GlobalExceptionMiddlewareTests.RecordingLogger<GlobalExceptionMiddleware>();
        var middleware = new GlobalExceptionMiddleware(
            async _ =>
            {
                await controller.Login(ValidViewModel(), default);
            },
            logger);

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(
            context.Response.Body,
            leaveOpen: true);
        var body = await reader.ReadToEndAsync();

        Assert.Equal(
            StatusCodes.Status500InternalServerError,
            context.Response.StatusCode);
        Assert.DoesNotContain("synthetic-detail", body, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(
            "Có lỗi hệ thống xảy ra.",
            json.RootElement.GetProperty("message").GetString());
        Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Unexpected_auth_service_failure_propagates_without_business_audit()
    {
        var expected = new InvalidDataException("synthetic repository failure");
        var authentication = new RecordingAuthenticationService();
        var audit = new RecordingAuditLogService();
        var controller = CreateController(
            FakeAuthService.Throwing(expected),
            authentication,
            audit);

        var actual = await Assert.ThrowsAsync<InvalidDataException>(
            () => controller.Login(ValidViewModel(), default));

        Assert.Same(expected, actual);
        Assert.Equal(0, authentication.SignInCalls);
        Assert.Equal(0, audit.WriteCalls);
        Assert.True(controller.ModelState.IsValid);
    }

    [Fact]
    public async Task Unexpected_auth_service_failure_through_global_owner_is_generic_500_once()
    {
        const string secret = "synthetic-auth-repository-secret";
        var controller = CreateController(
            FakeAuthService.Throwing(new IOException(secret)),
            new RecordingAuthenticationService(),
            new RecordingAuditLogService());
        var context = controller.HttpContext;
        context.TraceIdentifier = "trace-auth-technical";
        context.Response.Body = new MemoryStream();
        var logger =
            new GlobalExceptionMiddlewareTests.RecordingLogger<GlobalExceptionMiddleware>();
        var middleware = new GlobalExceptionMiddleware(
            async _ =>
            {
                await controller.Login(ValidViewModel(), default);
            },
            logger);

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(
            context.Response.Body,
            leaveOpen: true);
        var body = await reader.ReadToEndAsync();

        Assert.Equal(
            StatusCodes.Status500InternalServerError,
            context.Response.StatusCode);
        Assert.DoesNotContain(secret, body, StringComparison.Ordinal);
        Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Caller_cancellation_from_auth_service_propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var controller = CreateController(
            FakeAuthService.Throwing(
                new OperationCanceledException(cts.Token)),
            new RecordingAuthenticationService(),
            new RecordingAuditLogService());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => controller.Login(ValidViewModel(), cts.Token));
    }

    [Fact]
    public async Task Audit_failure_is_safe_best_effort_warning_after_sign_in()
    {
        var authentication = new RecordingAuthenticationService();
        var audit = new RecordingAuditLogService
        {
            WriteException = new IOException("synthetic audit detail")
        };
        var logger =
            new GlobalExceptionMiddlewareTests.RecordingLogger<AccountController>();
        var controller = CreateController(
            FakeAuthService.Returning(
                Result<LoginResponse>.Success(SuccessfulLogin())),
            authentication,
            audit,
            logger);

        var result = await controller.Login(ValidViewModel(), default);

        Assert.IsType<RedirectResult>(result);
        Assert.Equal(1, authentication.SignInCalls);
        Assert.Contains(
            logger.Entries,
            entry =>
                entry.Level == LogLevel.Warning &&
                entry.Message.Contains(nameof(IOException), StringComparison.Ordinal));
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Message.Contains(
                "synthetic audit detail",
                StringComparison.Ordinal));
    }

    private static AccountController CreateController(
        IAuthService authService,
        RecordingAuthenticationService authenticationService,
        RecordingAuditLogService auditLogService,
        GlobalExceptionMiddlewareTests.RecordingLogger<AccountController>? logger = null)
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddControllersWithViews();
        serviceCollection.AddSingleton<IAuthenticationService>(
            authenticationService);
        var services = serviceCollection.BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            RequestServices = services,
            TraceIdentifier = "trace-login"
        };
        var controller = new AccountController(
            authService,
            auditLogService,
            new FakeTerminalRepository(),
            new FakeCurrentStore(),
            logger ??
            new GlobalExceptionMiddlewareTests.RecordingLogger<AccountController>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = context
            }
        };

        return controller;
    }

    private static LoginVm ValidViewModel() => new()
    {
        UserName = "synthetic-user",
        Password = "synthetic-password"
    };

    private static LoginResponse SuccessfulLogin() => new()
    {
        UserId = 7,
        UserName = "synthetic-user",
        FullName = "Synthetic User",
        StoreId = 3,
        TerminalId = 5,
        TerminalCode = "POS-01",
        TerminalName = "POS 01",
        RoleId = 2,
        RoleCode = "CASHIER",
        RoleName = "Cashier"
    };

    private sealed class FakeAuthService(
        Func<CancellationToken, Task<Result<LoginResponse>>> handler)
        : IAuthService
    {
        public Task<Result<LoginResponse>> LoginAsync(
            LoginRequest request,
            CancellationToken ct = default) => handler(ct);

        public static FakeAuthService Returning(Result<LoginResponse> result)
            => new(_ => Task.FromResult(result));

        public static FakeAuthService Throwing(Exception exception)
            => new(_ => Task.FromException<Result<LoginResponse>>(exception));
    }

    private sealed class RecordingAuthenticationService : IAuthenticationService
    {
        public int SignInCalls { get; private set; }
        public Exception? SignInException { get; init; }

        public Task<AuthenticateResult> AuthenticateAsync(
            HttpContext context,
            string? scheme) =>
            Task.FromResult(AuthenticateResult.NoResult());

        public Task ChallengeAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task ForbidAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task SignInAsync(
            HttpContext context,
            string? scheme,
            ClaimsPrincipal principal,
            AuthenticationProperties? properties)
        {
            SignInCalls++;
            return SignInException == null
                ? Task.CompletedTask
                : Task.FromException(SignInException);
        }

        public Task SignOutAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties) =>
            Task.CompletedTask;
    }

    private sealed class RecordingAuditLogService : IAuditLogService
    {
        public int WriteCalls { get; private set; }
        public WriteAuditLogRequest? LastRequest { get; private set; }
        public Exception? WriteException { get; init; }

        public Task WriteAsync(
            WriteAuditLogRequest request,
            CancellationToken ct = default)
        {
            WriteCalls++;
            LastRequest = request;
            return WriteException == null
                ? Task.CompletedTask
                : Task.FromException(WriteException);
        }

        public Task<PagedResult<AuditLogListItemDto>> SearchAsync(
            AuditLogQueryDto query,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<AuditLogDetailDto?> GetDetailAsync(
            long id,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeTerminalRepository : IPOSTerminalRepository
    {
        public Task<POSTerminal?> GetByIdAsync(
            int id,
            CancellationToken ct = default) =>
            Task.FromResult<POSTerminal?>(null);

        public Task<POSTerminal?> GetByStoreAndIpAsync(
            int storeId,
            string ip,
            CancellationToken ct = default) =>
            Task.FromResult<POSTerminal?>(null);

        public Task<List<POSTerminal>> GetActiveByStoreAsync(
            int storeId,
            CancellationToken ct = default) =>
            Task.FromResult(new List<POSTerminal>());

        public Task<POSTerminal?> GetByDeviceKeyAsync(
            int storeId,
            string deviceKey,
            CancellationToken ct = default) =>
            Task.FromResult<POSTerminal?>(null);

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
