using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Audit;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Application.Interfaces.Services.Audit;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace GaoApp.Infrastructure.Services.Audit;

/// <summary>
/// Đọc ngữ cảnh thực thi hiện tại từ HttpContext + TenantContext.
/// 
/// Ưu tiên:
/// 1. Lấy StoreId từ ITenantContext vì GaoApp đang chạy multi-tenant theo store.
/// 2. Nếu không có thì fallback sang claim "store_id".
/// </summary>
public class HttpAuditExecutionContextAccessor : IAuditExecutionContextAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ITenantContext _tenantContext;

    public HttpAuditExecutionContextAccessor(
        IHttpContextAccessor httpContextAccessor,
        ITenantContext tenantContext)
    {
        _httpContextAccessor = httpContextAccessor;
        _tenantContext = tenantContext;
    }

    public AuditExecutionContextDto GetCurrent()
    {
        var httpContext = _httpContextAccessor.HttpContext;

        if (httpContext == null)
        {
            return new AuditExecutionContextDto
            {
                StoreId = _tenantContext.StoreId
            };
        }

        var user = httpContext.User;

        int? userId = null;
        var userIdRaw = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(userIdRaw, out var parsedUserId))
        {
            userId = parsedUserId;
        }

        // Ưu tiên TenantContext
        int? storeId = _tenantContext.StoreId;

        // Fallback sang claim nếu TenantContext chưa có
        if (!storeId.HasValue)
        {
            var storeIdRaw = user.FindFirstValue("store_id");
            if (int.TryParse(storeIdRaw, out var parsedStoreId))
            {
                storeId = parsedStoreId;
            }
        }

        return new AuditExecutionContextDto
        {
            StoreId = storeId,
            UserId = userId,
            UserName = user.Identity?.Name,
            TraceId = httpContext.TraceIdentifier,
            IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = httpContext.Request.Headers.UserAgent.ToString(),
            Path = httpContext.Request.Path.ToString()
        };
    }
}