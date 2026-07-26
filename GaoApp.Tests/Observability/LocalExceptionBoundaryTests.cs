using System.Text.Json;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Taxes;
using GaoApp.Application.Interfaces.Services.Taxes;
using GaoApp.Web.Areas.Admin.Controllers;
using GaoApp.Web.Areas.Admin.ViewModels.Taxes;
using GaoApp.Web.Middlewares;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GaoApp.Tests.Observability;

public sealed class LocalExceptionBoundaryTests
{
    private const string TechnicalDetail =
        "Server=private-sql;Password=synthetic-secret";

    [Fact]
    public async Task Technical_invalid_operation_from_form_controller_should_propagate()
    {
        var expected = new InvalidOperationException(TechnicalDetail);
        var controller = CreateController(new FakeTaxService
        {
            CreateException = expected
        });

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.Create(ValidViewModel(), default));

        Assert.Same(expected, actual);
        Assert.True(controller.ModelState.IsValid);
        Assert.DoesNotContain(
            controller.ModelState.Values.SelectMany(value => value.Errors),
            error => error.ErrorMessage.Contains(
                TechnicalDetail,
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            controller.TempData.Values.OfType<string>(),
            value => value.Contains(TechnicalDetail, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Expected_business_failure_from_form_controller_should_return_safe_view()
    {
        const string safeMessage = "Mã thuế đã tồn tại.";
        var controller = CreateController(new FakeTaxService
        {
            CreateException = new BusinessRuleException(safeMessage)
        });

        var result = await controller.Create(ValidViewModel(), default);

        Assert.IsType<ViewResult>(result);
        var error = Assert.Single(
            controller.ModelState[string.Empty]!.Errors);
        Assert.Equal(safeMessage, error.ErrorMessage);
    }

    [Fact]
    public async Task Technical_invalid_operation_from_json_controller_should_propagate()
    {
        var expected = new InvalidOperationException(TechnicalDetail);
        var controller = CreateController(new FakeTaxService
        {
            ToggleException = expected
        });

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.ToggleStatus(7, default));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task Expected_business_failure_from_json_controller_should_be_safe()
    {
        const string safeMessage = "Không thể đổi trạng thái thuế.";
        var controller = CreateController(new FakeTaxService
        {
            ToggleException = new BusinessRuleException(safeMessage)
        });

        var result = Assert.IsType<JsonResult>(
            await controller.ToggleStatus(7, default));
        var json = JsonSerializer.Serialize(result.Value);
        using var document = JsonDocument.Parse(json);
        var message = document.RootElement
            .GetProperty("message")
            .GetString();

        Assert.Equal(safeMessage, message);
        Assert.DoesNotContain(
            TechnicalDetail,
            json,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Form_technical_failure_through_global_owner_is_generic_500_once()
    {
        var expected = new InvalidOperationException(TechnicalDetail);
        var controller = CreateController(new FakeTaxService
        {
            CreateException = expected
        });
        var context = controller.HttpContext;
        context.Response.Body = new MemoryStream();
        var logger =
            new GlobalExceptionMiddlewareTests.RecordingLogger<GlobalExceptionMiddleware>();
        var middleware = new GlobalExceptionMiddleware(
            async _ =>
            {
                await controller.Create(ValidViewModel(), default);
            },
            logger);

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(
            context.Response.Body,
            leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        var error = Assert.Single(
            logger.Entries,
            entry => entry.Level == LogLevel.Error);

        Assert.Equal(
            StatusCodes.Status500InternalServerError,
            context.Response.StatusCode);
        Assert.Null(error.Exception);
        Assert.Equal(
            typeof(InvalidOperationException).FullName,
            error.StructuredState["ExceptionType"]);
        Assert.False(
            string.IsNullOrWhiteSpace(
                error.StructuredState["Fingerprint"]?.ToString()));
        Assert.DoesNotContain(
            TechnicalDetail,
            body,
            StringComparison.Ordinal);
    }

    private static TaxController CreateController(ITaxService taxService)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllersWithViews();
        services.AddSingleton<ITenantContext>(
            new FakeTenantContext());
        var provider = services.BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            RequestServices = provider,
            TraceIdentifier = "trace-local-boundary"
        };
        var controller = new TaxController(taxService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = context
            },
            TempData = new TempDataDictionary(
                context,
                new FakeTempDataProvider())
        };

        return controller;
    }

    private static TaxEditViewModel ValidViewModel() => new()
    {
        Code = "VAT10",
        Name = "VAT 10%",
        Rate = 10m,
        Status = true
    };

    private sealed class FakeTaxService : ITaxService
    {
        public Exception? CreateException { get; init; }
        public Exception? ToggleException { get; init; }

        public Task<PagedResult<TaxListItemDto>> GetPagedAsync(
            int storeId,
            string? search,
            int page,
            int pageSize,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<TaxEditDto?> GetForEditAsync(
            int storeId,
            int id,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<int> CreateAsync(
            int storeId,
            CreateTaxRequest dto,
            int? userId,
            CancellationToken ct = default) =>
            CreateException == null
                ? Task.FromResult(1)
                : Task.FromException<int>(CreateException);

        public Task<bool> UpdateAsync(
            int storeId,
            UpdateTaxRequest dto,
            int? userId,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> ToggleStatusAsync(
            int storeId,
            int id,
            int? userId,
            CancellationToken ct = default) =>
            ToggleException == null
                ? Task.FromResult(true)
                : Task.FromException<bool>(ToggleException);

        public Task<bool> SoftDeleteAsync(
            int storeId,
            int id,
            int? userId,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeTenantContext : ITenantContext
    {
        public int? StoreId => 3;
        public bool IsHostAdmin => false;
        public string? Subdomain => "synthetic-store";
    }

    private sealed class FakeTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(
            HttpContext context) =>
            new Dictionary<string, object>();

        public void SaveTempData(
            HttpContext context,
            IDictionary<string, object> values)
        {
        }
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } =
            Environments.Production;
        public string ApplicationName { get; set; } = "GaoApp.Tests";
        public string ContentRootPath { get; set; } =
            Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
