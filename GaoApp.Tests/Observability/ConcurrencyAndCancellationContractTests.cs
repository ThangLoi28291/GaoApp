using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.DTOs.POS;
using GaoApp.Application.DTOs.Taxes;
using GaoApp.Application.Interfaces.Repositories.Taxes;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Application.Services.Taxes;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Taxes;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Web.Areas.Admin.Controllers;
using GaoApp.Web.Middlewares;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GaoApp.Tests.Observability;

public sealed class ConcurrencyAndCancellationContractTests
{
    private const string DatabaseDetail =
        "Server=private-sql;Password=synthetic-secret";

    [Fact]
    public async Task Stock_documents_approve_concurrency_should_return_409()
    {
        var service = DispatchProxy.Create<
            IStockDocumentService,
            StockDocumentServiceProxy>();
        var proxy = (StockDocumentServiceProxy)(object)service;
        proxy.ApproveException = new DbUpdateConcurrencyException(DatabaseDetail);
        var controller = new StockDocumentsController(
            service,
            null!,
            null!,
            null!,
            null!,
            new AllowAuthorizationService())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var result = await controller.ApproveCommercial(
            7,
            new ApprovePurchaseReceiptCommercialRequest(),
            default);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        var responseJson = JsonSerializer.Serialize(conflict.Value);
        using var response = JsonDocument.Parse(responseJson);
        Assert.Contains(
            "Vui lòng tải lại",
            response.RootElement.GetProperty("message").GetString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            DatabaseDetail,
            responseJson,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Tax_update_concurrency_should_map_to_typed_409_without_error()
    {
        var repository = new FakeTaxRepository
        {
            SaveException = new ConcurrencyException(
                "Dữ liệu đã được thay đổi.",
                new InvalidOperationException(DatabaseDetail))
        };
        var service = new TaxService(repository);

        var (context, logger) = await InvokeMiddlewareAsync(
            _ => service.UpdateAsync(
                1,
                ValidTaxUpdate(),
                userId: null,
                default));

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        var body = await ReadBodyAsync(context);
        using var response = JsonDocument.Parse(body);
        Assert.Contains(
            "Vui lòng tải lại",
            response.RootElement.GetProperty("message").GetString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            DatabaseDetail,
            body,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Level == LogLevel.Error);
        Assert.Single(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Tax_repository_should_translate_ef_concurrency_without_raw_detail()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ThrowingConcurrencyDbContext(
            options,
            new TenantContext(),
            new StubCurrentUser());
        var repository = new TaxRepository(db);

        var exception = await Assert.ThrowsAsync<ConcurrencyException>(
            () => repository.SaveChangesAsync());

        Assert.Null(exception.InnerException);
        Assert.DoesNotContain(
            DatabaseDetail,
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Tax_validation_failure_should_remain_400()
    {
        var service = new TaxService(new FakeTaxRepository());
        var invalid = ValidTaxUpdate();
        invalid.Rate = 101m;

        var (context, logger) = await InvokeMiddlewareAsync(
            _ => service.UpdateAsync(1, invalid, userId: null, default));

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Tax_unexpected_invalid_operation_should_return_500()
    {
        var service = new TaxService(new FakeTaxRepository
        {
            SaveException = new InvalidOperationException(DatabaseDetail)
        });

        var (context, logger) = await InvokeMiddlewareAsync(
            _ => service.UpdateAsync(
                1,
                ValidTaxUpdate(),
                userId: null,
                default));

        Assert.Equal(
            StatusCodes.Status500InternalServerError,
            context.Response.StatusCode);
        var body = await ReadBodyAsync(context);
        Assert.DoesNotContain(
            DatabaseDetail,
            body,
            StringComparison.OrdinalIgnoreCase);
        var error = Assert.Single(
            logger.Entries,
            entry => entry.Level == LogLevel.Error);
        Assert.Null(error.Exception);
    }

    [Fact]
    public async Task Search_products_caller_cancellation_should_propagate_without_response()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var controller = CreatePosController(cancelSearch: true);
        var context = CreateHttpContext();
        context.RequestAborted = cts.Token;
        var logger = new RecordingLogger<GlobalExceptionMiddleware>();
        var middleware = new GlobalExceptionMiddleware(
            async _ =>
            {
                await controller.SearchProducts("gao", 20, cts.Token);
            },
            logger);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => middleware.InvokeAsync(context));

        Assert.Equal(0, context.Response.Body.Length);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Search_products_success_regression()
    {
        var controller = CreatePosController(cancelSearch: false);

        var result = await controller.SearchProducts("gao", 20, default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var items = Assert.IsType<List<POSProductSearchItemDto>>(ok.Value);
        Assert.Empty(items);
    }

    private static POSController CreatePosController(bool cancelSearch)
    {
        var service = DispatchProxy.Create<IPOSService, PosServiceProxy>();
        ((PosServiceProxy)(object)service).CancelSearch = cancelSearch;
        return new POSController(
            service,
            null!,
            null!,
            null!,
            null!);
    }

    private static async Task<(
        DefaultHttpContext Context,
        RecordingLogger<GlobalExceptionMiddleware> Logger)>
        InvokeMiddlewareAsync(Func<HttpContext, Task> action)
    {
        var context = CreateHttpContext();
        var logger = new RecordingLogger<GlobalExceptionMiddleware>();
        var middleware = new GlobalExceptionMiddleware(
            async current => await action(current),
            logger);

        await middleware.InvokeAsync(context);
        return (context, logger);
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "trace-r1.6-final3"
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/synthetic";
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(
            context.Response.Body,
            Encoding.UTF8,
            leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    private static UpdateTaxRequest ValidTaxUpdate() => new()
    {
        Id = 7,
        Code = "VAT10",
        Name = "VAT 10%",
        Rate = 10m,
        Status = true,
        RowVersion = [1, 2, 3]
    };

    private sealed class FakeTaxRepository : ITaxRepository
    {
        public Exception? SaveException { get; init; }

        public Task<(IReadOnlyList<Tax> Items, int TotalItems)> GetPagedAsync(
            int storeId,
            string? search,
            int page,
            int pageSize,
            CancellationToken ct = default) =>
            Task.FromResult<(
                IReadOnlyList<Tax> Items,
                int TotalItems)>(([], 0));

        public Task<(IReadOnlyList<Tax> Items, int TotalItems)> GetPagedAsync(
            int storeId,
            string? search,
            bool? status,
            int page,
            int pageSize,
            CancellationToken ct = default) =>
            GetPagedAsync(storeId, search, page, pageSize, ct);

        public Task<(int TotalItems, int ActiveItems, int InactiveItems)> GetSummaryAsync(
            int storeId,
            CancellationToken ct = default) =>
            Task.FromResult((0, 0, 0));

        public Task<Tax?> GetByIdAsync(
            int storeId,
            int id,
            CancellationToken ct = default) =>
            Task.FromResult<Tax?>(new Tax
            {
                Id = id,
                StoreId = storeId,
                Code = "VAT10",
                Name = "VAT 10%",
                Rate = 10m,
                IsActive = true
            });

        public Task<bool> ExistsCodeAsync(
            int storeId,
            string code,
            int? excludeId,
            CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<bool> ExistsNameAsync(
            int storeId,
            string name,
            int? excludeId,
            CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task AddAsync(
            Tax entity,
            CancellationToken ct = default) =>
            Task.CompletedTask;

        public void Remove(Tax entity)
        {
        }

        public Task SaveChangesAsync(CancellationToken ct = default) =>
            SaveException is null
                ? Task.CompletedTask
                : Task.FromException(SaveException);
    }

    private sealed class ThrowingConcurrencyDbContext(
        DbContextOptions<AppDbContext> options,
        ITenantContext tenant,
        ICurrentUser currentUser)
        : AppDbContext(options, tenant, currentUser)
    {
        public override Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromException<int>(
                new DbUpdateConcurrencyException(DatabaseDetail));
    }

    private sealed class StubCurrentUser : ICurrentUser
    {
        public int? UserId => null;
        public string? UserName => null;
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => false;
    }

    private class StockDocumentServiceProxy : DispatchProxy
    {
        public Exception? ApproveException { get; set; }

        protected override object? Invoke(
            MethodInfo? targetMethod,
            object?[]? args)
        {
            return targetMethod?.Name switch
            {
                nameof(IStockDocumentService.GetDetailAsync) =>
                    Task.FromResult<StockDocumentDto?>(new StockDocumentDto
                    {
                        Id = 7,
                        ReceiptSource = PurchaseReceiptSource.Direct
                    }),
                nameof(IStockDocumentService.ApproveCommercialAsync) =>
                    ApproveException is null
                        ? Task.CompletedTask
                        : Task.FromException(ApproveException),
                _ => throw new NotSupportedException(targetMethod?.Name)
            };
        }
    }

    private class PosServiceProxy : DispatchProxy
    {
        public bool CancelSearch { get; set; }

        protected override object? Invoke(
            MethodInfo? targetMethod,
            object?[]? args)
        {
            if (targetMethod?.Name !=
                nameof(IPOSService.SearchProductsForPOSAsync))
            {
                throw new NotSupportedException(targetMethod?.Name);
            }

            var ct = (CancellationToken)args![2]!;
            return CancelSearch
                ? Task.FromCanceled<List<POSProductSearchItemDto>>(ct)
                : Task.FromResult(new List<POSProductSearchItemDto>());
        }
    }

    private sealed class AllowAuthorizationService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements) =>
            Task.FromResult(AuthorizationResult.Success());

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            string policyName) =>
            Task.FromResult(AuthorizationResult.Success());
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        Exception? Exception);

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(
                logLevel,
                formatter(state, exception),
                exception));
        }
    }
}
