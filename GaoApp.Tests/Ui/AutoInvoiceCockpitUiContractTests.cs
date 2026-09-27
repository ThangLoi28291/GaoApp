using System.Reflection;
using System.Security.Claims;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Results;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Application.Services.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Web.Areas.Admin.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace GaoApp.Tests.Ui;

public sealed class AutoInvoiceCockpitUiContractTests
{
    private const string ViewPath = "GaoApp.Web/Areas/Admin/Views/AutoInvoice/Index.cshtml";
    private static readonly DateTime Now = new(2026, 9, 26, 18, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void View_exposes_eight_operational_tabs_and_all_existing_settings()
    {
        var view = Read(ViewPath);
        foreach (var title in new[] { "Hôm nay", "Chờ đủ tuổi", "Đơn lẻ", "Đợi gộp", "Sự cố", "Đã phát hành", "Tồn ngày trước", "Cấu hình" })
            Assert.Contains($"= \"{title}\"", view);
        foreach (var name in new[] { "IsEnabled", "MinimumAgeMinutes", "SeparateAmountThreshold", "GroupTargetAmount",
                     "SendIntervalSeconds", "ClosingTimeLocal", "IssueOldDayRemainder", "ScopeMode", "ScopeStartDateLocal",
                     "ScopeEndDateLocal", "TimeZoneId" })
            Assert.Contains($"name=\"{name}\"", view);
        Assert.Contains("asp-action=\"SaveSettings\"", view);
        Assert.Contains("asp-action=\"Toggle\"", view);
        Assert.Contains("Đang đợi target", view);
        Assert.Contains("Đã đạt target", view);
        Assert.Contains("Cutoff-ready", view);
        Assert.Contains("Old-day remainder", view);
        Assert.Contains("@row.SaleAtLocal", view);
        Assert.Contains("@row.EligibleAtLocal", view);
        Assert.Contains("@row.OperationStatus", view);
    }

    [Fact]
    public void Unknown_path_only_offers_supported_uuid_lookup_and_local_recheck_is_separate()
    {
        var view = Read(ViewPath);
        var unknown = view[view.IndexOf("@if (row.IsUnknown)", StringComparison.Ordinal)..
            view.IndexOf("else if (canOperate && row.CanRecheck)", StringComparison.Ordinal)];
        Assert.Contains("row.UuidLookupInvoiceHeadId.HasValue", unknown);
        Assert.Contains("asp-controller=\"Invoice\" asp-action=\"SyncViettelByUuid\"", unknown);
        Assert.Contains("value=\"@row.UuidLookupInvoiceHeadId\"", unknown);
        Assert.DoesNotContain("RecheckIncident", unknown);
        Assert.Contains("else if (canOperate && row.CanRecheck)", view);
        Assert.Contains("asp-action=\"RecheckIncident\"", view);
        Assert.Contains("Kiểm tra lại", view);
        Assert.Contains("@if (canRoute && row.CanChangeToManual)", view);
        Assert.Contains("asp-action=\"ChangeToManual\"", view);
        Assert.DoesNotContain("ViettelIssue", view);
        Assert.DoesNotContain("IssueManual", view);
        Assert.DoesNotContain("asp-action=\"RunOnce\"", view);
        Assert.DoesNotContain("Html.Raw", view);
        Assert.DoesNotContain("ManualWaitingInfo", view);
        Assert.Contains("Tự động", view);
        Assert.Contains("@row.ErrorMessage", view);
        var history = view[view.IndexOf("else if (tab == \"history\")", StringComparison.Ordinal)..
            view.IndexOf("Giờ cửa hàng:", StringComparison.Ordinal)];
        Assert.DoesNotContain("method=\"post\"", history);
    }

    [Theory]
    [InlineData("RecheckIncident", PermissionCodes.System.AutoInvoice.Operate)]
    [InlineData("ChangeToManual", PermissionCodes.System.Invoice.Route)]
    [InlineData("SaveSettings", PermissionCodes.System.AutoInvoice.Settings)]
    [InlineData("Toggle", PermissionCodes.System.AutoInvoice.Settings)]
    [InlineData("RunOnce", PermissionCodes.System.AutoInvoice.Operate)]
    public void Mutations_require_post_antiforgery_and_canonical_permission(string action, string permission)
    {
        var method = typeof(AutoInvoiceController).GetMethod(action)!;
        Assert.NotNull(method.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.Contains(method.GetCustomAttributes<AuthorizeAttribute>(), attribute => attribute.Policy == permission);
        Assert.Null(typeof(AutoInvoiceController).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        // Compatibility stays in the existing permission alias owner.
        var aliases = Read("GaoApp.Application/Common/Security/PermissionAliasMap.cs");
        Assert.Matches(@"(?s)\[PermissionCodes.System.AutoInvoice.Settings\].*?PermissionCodes.System.Integration.Manage", aliases);
    }

    [Fact]
    public async Task Today_uses_original_sale_day_in_configured_zone_not_invoice_or_stability_day()
    {
        var f = new Fixture();
        var today = f.Add(1, Now.AddHours(-1));
        today.InvoiceDate = Now.AddDays(-2);
        today.LastIssuanceRelevantChangeAtUtc = Now.AddMinutes(-1);
        var yesterday = f.Add(2, Now.AddHours(-3));
        yesterday.InvoiceDate = Now;
        yesterday.LastIssuanceRelevantChangeAtUtc = Now;
        var result = await f.Dashboard("today");
        Assert.Equal(new DateTime(2026, 9, 27), result.NowLocal.Date);
        Assert.Equal(1, result.Summary.TodayCount);
        Assert.Equal(1, Assert.Single(result.Queue).InvoiceHeadId);
        Assert.Equal(Now.AddHours(-1), result.Queue[0].SaleAtUtc);
        Assert.False(result.Queue[0].IsAgeEligible);
        Assert.Equal(2, Assert.Single((await f.Dashboard("old")).Queue).InvoiceHeadId);
        Assert.Empty(f.Writes);
    }

    [Fact]
    public async Task Ready_lanes_respect_stable_age_threshold_safe_details_route_and_coverage()
    {
        var f = new Fixture();
        f.Add(1).LastIssuanceRelevantChangeAtUtc = Now.AddMinutes(-29);
        f.Add(2).LastIssuanceRelevantChangeAtUtc = Now.AddMinutes(-30);
        f.Add(3).GrandTotal = 99;
        f.Add(4).Details.Single().Quantity = 0;
        f.Add(5).LastErrorMessage = "Local error";
        f.Add(6);
        f.Operation(6, AutoInvoiceOperationStatus.Processing);
        f.Add(7);
        f.Operation(7, AutoInvoiceOperationStatus.Succeeded);
        f.Add(8).Order!.InvoiceIssuanceRoute = InvoiceIssuanceRoute.Manual;
        f.Add(9).ProviderStatus = InvoiceProviderStatus.Issued;
        f.Add(10).IssuedAtUtc = Now;
        f.Add(11).ProviderStatus = InvoiceProviderStatus.DraftSent;
        f.Add(12).Details.Single().TotalAmount = 0;
        f.Add(13).Details.Single().IsDeleted = true;
        f.Add(14).StoreId = 2;
        f.Add(15).Order = null;
        Assert.Equal(1, Assert.Single((await f.Dashboard("waiting")).Queue).InvoiceHeadId);
        Assert.Equal(2, Assert.Single((await f.Dashboard("single")).Queue).InvoiceHeadId);
        var group = await f.Dashboard("groups");
        Assert.Equal(3, Assert.Single(group.Queue).InvoiceHeadId);
        Assert.Equal(new[] { 3 }, Assert.Single(group.Groups).InvoiceHeadIds);
        var all = await f.Dashboard("today");
        Assert.All(all.Queue, row => Assert.Equal(InvoiceIssuanceRoute.Automatic, row.InvoiceIssuanceRoute));
        Assert.DoesNotContain(all.Queue, row => new[] { 7, 8, 9, 10, 14, 15 }.Contains(row.InvoiceHeadId));
        Assert.False(Assert.Single(all.Queue, row => row.InvoiceHeadId == 6).CanChangeToManual);
        Assert.Empty(f.Writes);
    }

    [Fact]
    public async Task Stability_falls_back_to_completed_sale_then_invoice_date()
    {
        var f = new Fixture();
        f.Add(1, Now.AddMinutes(-30));
        var fallback = f.Add(2, Now.AddMinutes(-10));
        fallback.Order!.CompletedAtUtc = null;
        var result = await f.Dashboard("today");
        Assert.Equal(Now.AddMinutes(-30), Assert.Single(result.Queue, row => row.InvoiceHeadId == 1).StableAtUtc);
        Assert.True(Assert.Single(result.Queue, row => row.InvoiceHeadId == 1).IsAgeEligible);
        Assert.Equal(fallback.InvoiceDate, Assert.Single(result.Queue, row => row.InvoiceHeadId == 2).StableAtUtc);
        Assert.False(Assert.Single(result.Queue, row => row.InvoiceHeadId == 2).IsAgeEligible);
    }

    [Fact]
    public async Task Groups_keep_compatibility_boundaries_and_expose_target_cutoff_and_old_remainder()
    {
        var f = new Fixture();
        f.Add(1).GrandTotal = 60;
        f.Add(2).GrandTotal = 50;
        f.Add(3).GrandTotal = 40;
        f.Heads[2].LegalEntityId = 2;
        f.Add(4).GrandTotal = 40;
        f.Heads[3].InvoiceSeries = "DIFFERENT";
        f.Add(5).GrandTotal = 40;
        f.Heads[4].LastIssuanceRelevantChangeAtUtc = Now;
        f.Add(6).GrandTotal = 100;
        var groups = (await f.Dashboard("groups")).Groups;
        Assert.Equal(3, groups.Count);
        var target = Assert.Single(groups, group => group.InvoiceHeadIds.Contains(1));
        Assert.Equal(new[] { 1, 2 }, target.InvoiceHeadIds);
        Assert.True(target.IsReadyByTarget);
        Assert.False(target.IsReadyByClosing);
        Assert.False(target.IsOldDayRemainder);
        Assert.All(groups.Where(group => !group.InvoiceHeadIds.Contains(1)), group => Assert.False(group.IsReadyByTarget));
        f.Settings.ClosingTimeLocal = TimeSpan.Zero;
        Assert.All((await f.Dashboard("groups")).Groups, group => Assert.True(group.IsReadyByClosing));
        f.Add(7, Now.AddDays(-2)).GrandTotal = 10;
        var old = Assert.Single((await f.Dashboard("old")).Groups);
        Assert.True(old.IsOldDayRemainder);
        Assert.False(old.IsReadyByClosing);
        Assert.False(old.IsReadyByTarget);
        f.Settings.IssueOldDayRemainder = false;
        Assert.False(Assert.Single((await f.Dashboard("old")).Groups).IsOldDayRemainder);
        Assert.Empty(f.Writes);
    }

    [Theory]
    [InlineData("Invoice.UnitMissing", true)]
    [InlineData("Invoice.InputInvoiceStockInsufficient", true)]
    [InlineData("InvoiceBuyer.InformationRequired", true)]
    [InlineData("InvoiceProvider.NotConfigured", true)]
    [InlineData("InvoiceProvider.CredentialKeyUnavailable", true)]
    [InlineData("OTHER_PROVIDER_ERROR", false)]
    [InlineData("HTTP_503", false)]
    [InlineData("UNKNOWN_RESULT", false)]
    public async Task Only_local_recoverable_incidents_offer_recheck(string code, bool canRecheck)
    {
        var f = new Fixture();
        f.Add(1).LastErrorCode = code;
        var row = Assert.Single((await f.Dashboard("errors")).Queue);
        Assert.True(row.HasIncident);
        Assert.Equal(canRecheck, row.CanRecheck);
        Assert.False(row.IsReady);
        if (code is "HTTP_503" or "UNKNOWN_RESULT")
        {
            Assert.True(row.IsUnknown);
            Assert.False(row.CanChangeToManual);
            Assert.Null(row.UuidLookupInvoiceHeadId);
        }
        Assert.Empty(f.Writes);
    }

    [Fact]
    public async Task Unknown_operation_uses_group_target_uuid_and_suppresses_recheck_and_route_change()
    {
        var f = new Fixture();
        f.Add(1).LastErrorCode = "Invoice.UnitMissing";
        var operation = f.Operation(1, AutoInvoiceOperationStatus.Unknown);
        operation.Kind = AutoInvoiceOperationKind.Group;
        operation.InvoiceHeadId = 900;
        var target = f.Add(900);
        target.IsAutoInvoiceGroup = true;
        target.TransactionUuid = Guid.NewGuid().ToString();
        var row = Assert.Single((await f.Dashboard("errors")).Queue);
        Assert.True(row.IsUnknown);
        Assert.Equal(900, row.UuidLookupInvoiceHeadId);
        Assert.False(row.CanRecheck);
        Assert.False(row.CanChangeToManual);
        target.TransactionUuid = null;
        Assert.Null(Assert.Single((await f.Dashboard("errors")).Queue).UuidLookupInvoiceHeadId);
        Assert.Empty(f.Writes);
    }

    [Fact]
    public async Task Issuing_without_operation_never_exposes_generic_recheck_or_uuid_button()
    {
        var f = new Fixture();
        f.Add(1).ProviderStatus = InvoiceProviderStatus.Issuing;
        var row = Assert.Single((await f.Dashboard("errors")).Queue);
        Assert.True(row.IsUnknown);
        Assert.False(row.CanRecheck);
        Assert.False(row.CanChangeToManual);
        Assert.Null(row.UuidLookupInvoiceHeadId);
    }

    [Fact]
    public async Task History_keeps_automatic_operations_and_projection_paging_is_bounded()
    {
        var f = new Fixture();
        f.Operation(1, AutoInvoiceOperationStatus.Succeeded);
        f.Operation(2, AutoInvoiceOperationStatus.Succeeded).IsManual = true;
        for (var id = 3; id <= 7; id++) f.Add(id);
        var result = await f.Service.GetDashboardAsync(new AutoInvoiceDashboardQueryDto { Workspace = "today", Page = 2, PageSize = 2 });
        Assert.Equal(5, result.TotalRows);
        Assert.Equal(new[] { 5, 6 }, result.Queue.Select(row => row.InvoiceHeadId).ToArray());
        Assert.Single(result.History);
        Assert.Equal(1, result.Summary.IssuedCount);
        Assert.Empty(f.Writes);
    }

    [Fact]
    public async Task Recheck_controller_calls_existing_service_and_returns_to_incidents()
    {
        var f = new Fixture();
        f.Add(1).LastErrorCode = "Invoice.UnitMissing";
        var calls = new List<int>();
        var service = Proxy<IAutoInvoiceService>((method, args) =>
        {
            Assert.Equal(nameof(IAutoInvoiceService.RecheckIncidentAsync), method.Name);
            calls.Add((int)args[0]!);
            return Task.FromResult(Result.Success());
        });
        var controller = Controller(service);
        var result = Assert.IsType<RedirectToActionResult>(await controller.RecheckIncident(1, new Tenant(), f.Repository, default));
        Assert.Equal(new[] { 1 }, calls);
        Assert.Equal("Index", result.ActionName);
        Assert.Equal("errors", result.RouteValues!["tab"]);
        Assert.NotNull(controller.TempData["Success"]);
    }

    [Theory]
    [InlineData("safe", true)]
    [InlineData("manual", false)]
    [InlineData("active", false)]
    [InlineData("unknown-operation", false)]
    [InlineData("issued", false)]
    [InlineData("issuing", false)]
    [InlineData("covered", false)]
    [InlineData("foreign-store", false)]
    [InlineData("missing", false)]
    public async Task Route_controller_resolves_invoice_order_and_delegates_only_safe_automatic_changes(string state, bool allowed)
    {
        var f = new Fixture();
        var head = f.Add(1);
        head.OrderId = 42;
        head.Order!.Id = 42;
        switch (state)
        {
            case "manual": head.Order.InvoiceIssuanceRoute = InvoiceIssuanceRoute.Manual; break;
            case "active": f.Operation(1, AutoInvoiceOperationStatus.Processing); break;
            case "unknown-operation": f.Operation(1, AutoInvoiceOperationStatus.Unknown); break;
            case "issued": head.ProviderStatus = InvoiceProviderStatus.Issued; break;
            case "issuing": head.ProviderStatus = InvoiceProviderStatus.Issuing; break;
            case "covered": f.Operation(1, AutoInvoiceOperationStatus.Succeeded); break;
            case "foreign-store": head.StoreId = 2; break;
            case "missing": f.Heads.Clear(); break;
        }
        var calls = 0;
        var route = Proxy<IInvoiceIssuanceRouteService>((method, args) =>
        {
            Assert.Equal(nameof(IInvoiceIssuanceRouteService.ChangeRouteAsync), method.Name);
            Assert.Equal(42, args[0]);
            Assert.Equal(InvoiceIssuanceRoute.Manual, args[1]);
            calls++;
            return Task.FromResult(Result<InvoiceIssuanceRouteDto>.Success(new() { OrderId = 42, Route = InvoiceIssuanceRoute.Manual }));
        });
        var controller = Controller(Proxy<IAutoInvoiceService>((_, _) => throw new InvalidOperationException("No issue service call allowed.")));
        Assert.IsType<RedirectToActionResult>(await controller.ChangeToManual(1, new Tenant(), f.Repository, route, default));
        Assert.Equal(allowed ? 1 : 0, calls);
        Assert.Equal(state == "manual" ? InvoiceIssuanceRoute.Manual : InvoiceIssuanceRoute.Automatic, head.Order.InvoiceIssuanceRoute);
        Assert.Empty(f.Writes);
    }

    [Theory]
    [InlineData("settings", PermissionCodes.System.AutoInvoice.Settings)]
    [InlineData("today", PermissionCodes.System.AutoInvoice.Operate)]
    public async Task Index_checks_workspace_permission_before_loading_data(string tab, string expectedPermission)
    {
        var auth = Proxy<IAuthorizationService>((_, args) =>
        {
            Assert.Equal(expectedPermission, args[2]);
            return Task.FromResult(AuthorizationResult.Failed());
        });
        var controller = Controller(Proxy<IAutoInvoiceService>((_, _) => throw new InvalidOperationException("Unauthorized read.")));
        Assert.IsType<ForbidResult>(await controller.Index(tab, null, null, null, null, default, auth));
    }

    [Fact]
    public async Task Settings_toggle_returns_to_settings_for_settings_only_users()
    {
        var controller = Controller(Proxy<IAutoInvoiceService>((method, _) =>
        {
            Assert.Equal(nameof(IAutoInvoiceService.ToggleEnabledAsync), method.Name);
            return Task.FromResult(Result<bool>.Success(true));
        }));
        var result = Assert.IsType<RedirectToActionResult>(await controller.Toggle(default));
        Assert.Equal("settings", result.RouteValues!["tab"]);
    }

    private sealed class Fixture
    {
        public AutoInvoiceSettings Settings { get; } = new()
        {
            StoreId = 1, IsEnabled = true, MinimumAgeMinutes = 30, SeparateAmountThreshold = 100,
            GroupTargetAmount = 100, ClosingTimeLocal = new TimeSpan(23, 0, 0), IssueOldDayRemainder = true,
            TimeZoneId = "SE Asia Standard Time", ScopeMode = AutoInvoiceScopeMode.Today
        };
        public List<InvoiceHead> Heads { get; } = new();
        public List<AutoInvoiceOperation> Operations { get; } = new();
        public List<string> Writes { get; } = new();
        public IAutoInvoiceRepository Repository { get; }
        public AutoInvoiceService Service { get; }

        public Fixture()
        {
            Repository = Proxy<IAutoInvoiceRepository>((method, args) => method.Name switch
            {
                nameof(IAutoInvoiceRepository.GetSettingsAsync) => Task.FromResult<AutoInvoiceSettings?>(Settings),
                nameof(IAutoInvoiceRepository.GetCandidateInvoicesAsync) => Task.FromResult(Heads.ToList()),
                nameof(IAutoInvoiceRepository.GetActiveOperationsAsync) => Task.FromResult(Operations.Where(operation => operation.Status is
                    AutoInvoiceOperationStatus.Pending or AutoInvoiceOperationStatus.Processing or AutoInvoiceOperationStatus.Unknown).ToList()),
                nameof(IAutoInvoiceRepository.GetOperationsAsync) => Task.FromResult(Operations.Take((int)args[1]!).ToList()),
                nameof(IAutoInvoiceRepository.GetWorkerStateAsync) => Task.FromResult<AutoInvoiceWorkerState?>(null),
                nameof(IAutoInvoiceRepository.GetInvoiceHeadForAutomaticIssueAsync) or nameof(IAutoInvoiceRepository.GetInvoiceHeadForClaimAsync) =>
                    Task.FromResult(Heads.FirstOrDefault(head => head.Id == (int)args[1]! && head.StoreId == (int)args[0]!)),
                nameof(IAutoInvoiceRepository.HasActiveSourceAsync) => Task.FromResult(Operations.SelectMany(operation => operation.Sources)
                    .Any(source => source.InvoiceHeadId == (int)args[1]! && source.IsActive)),
                nameof(IAutoInvoiceRepository.HasSuccessfulSourceAsync) => Task.FromResult(Operations.SelectMany(operation => operation.Sources)
                    .Any(source => source.InvoiceHeadId == (int)args[1]! && source.Status == AutoInvoiceSourceStatus.Succeeded)),
                _ => RejectWrite(method.Name)
            });
            Service = new AutoInvoiceService(Repository, null!, null!, null!, null!, new Tenant(), new User(), new Clock());
        }

        private object RejectWrite(string name)
        {
            Writes.Add(name);
            throw new InvalidOperationException($"Dashboard must not call {name}.");
        }

        public Task<AutoInvoiceDashboardDto> Dashboard(string workspace) =>
            Service.GetDashboardAsync(new AutoInvoiceDashboardQueryDto { Workspace = workspace });

        public InvoiceHead Add(int id, DateTime? sale = null)
        {
            var head = new InvoiceHead
            {
                Id = id, StoreId = 1, OrderId = id, InvoiceDate = sale ?? Now.AddHours(-1),
                Order = new Order { Id = id, StoreId = 1, Status = OrderStatus.Completed, CompletedAtUtc = sale ?? Now.AddHours(-1),
                    InvoiceIssuanceRoute = InvoiceIssuanceRoute.Automatic },
                GrandTotal = 100, ProviderStatus = InvoiceProviderStatus.LocalDraft, InvoiceProviderSettingId = 10,
                InvoiceProviderSetting = new InvoiceProviderSetting { Id = 10, StoreId = 1, ProviderCode = "VIETTEL", IsActive = true },
                Details = new List<InvoiceDetail> { new() { Id = id, StoreId = 1, ItemName = "Item", UnitName = "Unit", Quantity = 1, TotalAmount = 100 } }
            };
            Heads.Add(head);
            return head;
        }

        public AutoInvoiceOperation Operation(int sourceId, AutoInvoiceOperationStatus status)
        {
            var operation = new AutoInvoiceOperation
            {
                Id = Operations.Count + 1, StoreId = 1, InvoiceHeadId = sourceId, Status = status,
                Sources = new List<AutoInvoiceOperationSource> { new() { StoreId = 1, InvoiceHeadId = sourceId,
                    Status = status == AutoInvoiceOperationStatus.Succeeded ? AutoInvoiceSourceStatus.Succeeded : AutoInvoiceSourceStatus.Claimed,
                    IsActive = status != AutoInvoiceOperationStatus.Succeeded } }
            };
            Operations.Add(operation);
            return operation;
        }
    }

    private static AutoInvoiceController Controller(IAutoInvoiceService service)
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity("test")) };
        return new AutoInvoiceController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, Proxy<ITempDataProvider>((method, _) => method.Name == "LoadTempData"
                ? new Dictionary<string, object>() : null))
        };
    }

    private static T Proxy<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, CallProxy>();
        ((CallProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    public class CallProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args ?? Array.Empty<object?>());
    }

    private static string Read(string path)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "GaoApp.sln")))
                return File.ReadAllText(Path.Combine(directory.FullName, path));
        throw new InvalidOperationException("Could not find GaoApp repository root.");
    }

    private sealed class Tenant : ITenantContext
    {
        public int? StoreId => 1;
        public bool IsHostAdmin => false;
        public string? Subdomain => "test";
    }

    private sealed class User : ICurrentUser
    {
        public int? UserId => 9;
        public string? UserName => "cockpit-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }
}
