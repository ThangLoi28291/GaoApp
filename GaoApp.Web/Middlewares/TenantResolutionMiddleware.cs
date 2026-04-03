using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Options;
using GaoApp.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
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
/// - Nếu DB lỗi => trả 503 Service Unavailable
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
    public TenantResolutionMiddleware(
        RequestDelegate next,
        ILogger<TenantResolutionMiddleware> logger,
        IOptions<TenantOptions> tenantOptions,
        IWebHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _tenantOptions = tenantOptions.Value;
        _env = env;
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

        _logger.LogInformation(
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
        // Cho phép localhost/127.0.0.1 chạy local không bắt tenant
        // Giữ nguyên behavior cũ để không phá môi trường dev hiện tại
        // =========================================================
        if ((host == "localhost" || host == "127.0.0.1") && _env.IsDevelopment())
        {
            var fakeTenant = context.Request.Query["tenant"].ToString();

            if (!string.IsNullOrWhiteSpace(fakeTenant))
            {
                var normalized = fakeTenant.Trim().ToLowerInvariant();

                var store = await dbContext.Stores
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.SubDomainNormalized == normalized);

                if (store != null)
                {
                    tenantContextWriter.SetStore(store.Id, normalized);
                }
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

        try
        {
            var store = await dbContext.Stores
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.SubDomainNormalized == normalizedSubdomain,
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
        }
        catch (SqlException ex)
        {
            _logger.LogError(
                ex,
                "Tenant resolution failed due to SQL error. Host={Host}, Subdomain={Subdomain}",
                host,
                normalizedSubdomain);

            await WriteProblemAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "Hệ thống đang tạm thời không kết nối được cơ sở dữ liệu. Vui lòng thử lại sau.");
            return;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(
                ex,
                "Tenant resolution failed due to DbUpdateException. Host={Host}, Subdomain={Subdomain}",
                host,
                normalizedSubdomain);

            await WriteProblemAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "Dịch vụ dữ liệu đang tạm thời không sẵn sàng. Vui lòng thử lại sau.");
            return;
        }
        catch (OperationCanceledException ex) when (context.RequestAborted.IsCancellationRequested)
        {
            _logger.LogWarning(
                ex,
                "Tenant resolution cancelled by client. Host={Host}, Subdomain={Subdomain}",
                host,
                normalizedSubdomain);
            return;
        }

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
}