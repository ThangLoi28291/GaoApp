using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Options;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Configuration;
using GaoApp.Web.Services.Acb;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.Middlewares;

/// <summary>
/// Middleware resolve tenant/store theo host/subdomain.
///
/// Mục tiêu:
/// - đọc host hiện tại
/// - xác định tenant tương ứng
/// - ghi vào TenantContext để các layer dưới dùng
///
/// Lưu ý:
/// - Lỗi DB/framework được propagate tới global exception owner
/// - Nếu host/tenant không hợp lệ => trả lỗi business phù hợp
/// - Bỏ qua /health để health endpoint không phụ thuộc tenant
/// - Host/Scheme/IP ở đây đã được normalize nếu UseForwardedHeaders
///   được đặt đúng thứ tự trong pipeline
/// </summary>
public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantResolutionMiddleware> _logger;
    private readonly TenantOptions _tenantOptions;
    private readonly IWebHostEnvironment _env;
    private readonly AcbCallbackRoutingOptions _callbackRouting;
    public TenantResolutionMiddleware(
        RequestDelegate next,
        ILogger<TenantResolutionMiddleware> logger,
        IOptions<TenantOptions> tenantOptions,
        IWebHostEnvironment env,
        IOptions<AcbCallbackRoutingOptions>? callbackRouting = null)
    {
        _next = next;
        _logger = logger;
        _tenantOptions = tenantOptions.Value;
        _env = env;
        _callbackRouting = callbackRouting?.Value ?? new AcbCallbackRoutingOptions();
    }

    public async Task InvokeAsync(
        HttpContext context,
        AppDbContext dbContext,
        ITenantContextWriter tenantContextWriter)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var host = context.Request.Host.Host?.Trim().ToLowerInvariant();

        _logger.LogDebug(
            "Tenant request info. Scheme={Scheme}, Host={Host}, RemoteIp={RemoteIp}, Path={Path}",
            context.Request.Scheme,
            context.Request.Host.Value,
            context.Connection.RemoteIpAddress?.ToString(),
            context.Request.Path.Value);

        if (string.IsNullOrWhiteSpace(host))
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status400BadRequest,
                "Host không hợp lệ.");
            return;
        }

        // =========================================================
        // Resolve cửa hàng mặc định trên mọi request local, kể cả sau redirect.
        // Query tenant vẫn được ưu tiên để hỗ trợ kiểm thử trong Development.
        // =========================================================
        if ((host == "localhost" || host == "127.0.0.1") && _env.IsDevelopment())
        {
            var fakeTenant = context.Request.Query["tenant"].ToString();

            if (string.IsNullOrWhiteSpace(fakeTenant))
            {
                fakeTenant = _tenantOptions.DevelopmentDefaultSubdomain;
            }

            if (!string.IsNullOrWhiteSpace(fakeTenant))
            {
                var normalized = fakeTenant.Trim().ToLowerInvariant();

                var localStore = await dbContext.Stores
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x =>
                            x.SubDomainNormalized == normalized &&
                            x.IsActive,
                        context.RequestAborted);

                if (localStore == null)
                {
                    await WriteProblemAsync(
                        context,
                        StatusCodes.Status404NotFound,
                        $"Không tìm thấy cửa hàng đang hoạt động với subdomain '{normalized}'. Kiểm tra Tenant:DevelopmentDefaultSubdomain hoặc tham số tenant.");
                    return;
                }

                tenantContextWriter.SetStore(localStore.Id, normalized);
                BindStoreItems(context, localStore);
            }

            await _next(context);
            return;
        }

        var rootDomain = (_tenantOptions.RootDomain ?? string.Empty).Trim().ToLowerInvariant();
        var adminSubdomain = (_tenantOptions.AdminSubdomain ?? "admin").Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(rootDomain))
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "Tenant root domain chưa được cấu hình.");
            return;
        }

        // Resolve only configured bank callback POSTs before ordinary host routing.
        // Never change Request.Host or use body/query/header values to choose a store.
        if (HttpMethods.IsPost(context.Request.Method) && AcbCallbackEndpoint.IsCallbackPath(context.Request.Path) &&
            _callbackRouting.Handles(host))
        {
            var route = await dbContext.Set<AcbCallbackRoute>().AsNoTracking()
                .SingleOrDefaultAsync(x => x.Host == host, context.RequestAborted);
            context.Items[AcbCallbackEndpoint.RoutedStoreItem] = route?.TargetStoreId;
            if (route?.TargetStoreId is not > 0)
            {
                context.Items["AcbCallbackOutcome"] = "CALLBACK_STORE_NOT_SELECTED";
                await WriteProblemAsync(context, StatusCodes.Status503ServiceUnavailable,
                    "Chưa chọn cửa hàng nhận callback tại URL cũ hoặc cấu hình đang tạm ngưng.");
                return;
            }
            var mappedStore = await dbContext.Stores.AsNoTracking().SingleOrDefaultAsync(
                x => x.Id == route.TargetStoreId && x.IsActive, context.RequestAborted);
            if (mappedStore == null)
            {
                context.Items["AcbCallbackOutcome"] = "CALLBACK_STORE_NOT_FOUND";
                // A configured alias must never fall back to a different store on this host.
                await WriteProblemAsync(context, StatusCodes.Status503ServiceUnavailable,
                    "Cửa hàng nhận callback chưa tồn tại hoặc chưa hoạt động.");
                return;
            }
            tenantContextWriter.SetStore(mappedStore.Id, mappedStore.SubDomainNormalized);
            BindStoreItems(context, mappedStore);
            await _next(context); // The existing callback controller still authenticates this store's key.
            return;
        }

        // =========================================================
        // Host admin: admin.gaomart.com.vn
        // Phải set HostAdmin rõ ràng để pipeline dưới nhận biết
        // =========================================================
        var expectedAdminHost = $"{adminSubdomain}.{rootDomain}";
        if (string.Equals(host, expectedAdminHost, StringComparison.OrdinalIgnoreCase))
        {
            tenantContextWriter.SetHostAdmin();
            await _next(context);
            return;
        }

        // =========================================================
        // Chỉ chấp nhận host thuộc root domain hệ thống
        // Ví dụ:
        // - store1.gaomart.com.vn => hợp lệ
        // - abc.otherdomain.com   => không hợp lệ
        // =========================================================
        if (!host.EndsWith($".{rootDomain}", StringComparison.OrdinalIgnoreCase))
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status400BadRequest,
                "Host không thuộc domain hệ thống.");
            return;
        }

        var subdomain = GetSubdomain(host, rootDomain);

        if (string.IsNullOrWhiteSpace(subdomain))
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status400BadRequest,
                "Không xác định được tenant từ domain.");
            return;
        }

        var normalizedSubdomain = subdomain.Trim().ToLowerInvariant();

        var store = await dbContext.Stores
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x =>
                    x.SubDomainNormalized == normalizedSubdomain &&
                    x.IsActive,
                context.RequestAborted);

        if (store == null)
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status404NotFound,
                $"Không tìm thấy tenant '{normalizedSubdomain}'.");
            return;
        }

        // IMPORTANT:
        // Set đúng subdomain, không set store.Name.
        tenantContextWriter.SetStore(store.Id, normalizedSubdomain);

        // Reuse the Store already resolved for this request. Downstream
        // terminal resolution validates this ID against ICurrentStore
        // before trusting it, avoiding a duplicate read of Stores.
        context.Items["CurrentStoreId"] = store.Id.ToString();
        context.Items["CurrentStoreName"] = store.Name;

        await _next(context);
    }

    /// <summary>
    /// Tách subdomain từ host dựa trên root domain.
    /// Ví dụ:
    /// - minhhung.gaomart.com.vn với root gaomart.com.vn => minhhung
    /// - admin.gaomart.com.vn với root gaomart.com.vn => admin
    /// </summary>
    private static string? GetSubdomain(string host, string rootDomain)
    {
        var suffix = "." + rootDomain;

        if (!host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return null;

        var tenantPart = host[..^suffix.Length];

        if (string.IsNullOrWhiteSpace(tenantPart))
            return null;

        // Không chấp nhận host nhiều tầng kiểu a.b.gaomart.com.vn ở giai đoạn này
        if (tenantPart.Contains('.'))
            return null;

        return tenantPart;
    }

    /// <summary>
    /// Ghi response JSON thân thiện, tránh lộ stack trace.
    /// </summary>
    private static async Task WriteProblemAsync(
        HttpContext context,
        int statusCode,
        string message)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            status = statusCode,
            message = message
        };

        await context.Response.WriteAsJsonAsync(payload);
    }
    private static void BindStoreItems(HttpContext context, Store store)
    {
        context.Items["CurrentStoreId"] = store.Id.ToString();
        context.Items["CurrentStoreName"] = store.Name;
    }
}
