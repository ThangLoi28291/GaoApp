using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Interfaces.Repositories.POSTerminals;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Middlewares;

public class TerminalResolutionMiddleware : IMiddleware
{
    private const string PosDeviceKeyCookieName = "POS_DEVICE_KEY";

    private readonly IPOSTerminalRepository _terminalRepository;
    private readonly ICurrentStore _currentStore;
    private readonly AppDbContext _db;
    private readonly ILogger<TerminalResolutionMiddleware> _logger;

    public TerminalResolutionMiddleware(
        IPOSTerminalRepository terminalRepository,
        ICurrentStore currentStore,
        AppDbContext db,
        ILogger<TerminalResolutionMiddleware> logger)
    {
        _terminalRepository = terminalRepository;
        _currentStore = currentStore;
        _db = db;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // HttpContext.Items is request-scoped. Preserve the Store resolved by
        // TenantResolutionMiddleware; reset only terminal-specific values.
        context.Items["CurrentTerminalId"] = null;
        context.Items["CurrentTerminalName"] = null;
        context.Items["CurrentTerminalCode"] = null;

        try
        {
            var storeId = _currentStore.StoreId;

            if (storeId > 0)
            {
                var hasMatchingResolvedStore =
                    int.TryParse(
                        context.Items["CurrentStoreId"]?.ToString(),
                        out var resolvedStoreId) &&
                    resolvedStoreId == storeId &&
                    !string.IsNullOrWhiteSpace(
                        context.Items["CurrentStoreName"]?.ToString());

                // Fallback keeps this middleware safe if it is ever invoked
                // without TenantResolutionMiddleware earlier in the pipeline.
                if (!hasMatchingResolvedStore)
                {
                    var store = await _db.Stores
                        .AsNoTracking()
                        .FirstOrDefaultAsync(
                            x => x.Id == storeId && x.IsActive,
                            context.RequestAborted);

                    if (store != null)
                    {
                        context.Items["CurrentStoreId"] = store.Id.ToString();
                        context.Items["CurrentStoreName"] = store.Name;
                    }
                }

                // Resolve terminal bằng Cookie DeviceKey, không dùng IP nữa.
                var deviceKey = context.Request.Cookies[PosDeviceKeyCookieName];

                if (!string.IsNullOrWhiteSpace(deviceKey))
                {
                    var terminal = await _terminalRepository.GetByDeviceKeyAsync(
                        storeId,
                        deviceKey,
                        context.RequestAborted);

                    if (terminal != null)
                    {
                        context.Items["CurrentTerminalId"] = terminal.Id.ToString();
                        context.Items["CurrentTerminalName"] = terminal.Name;
                        context.Items["CurrentTerminalCode"] = terminal.Code;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Không chặn request nếu resolve terminal lỗi.
            // Login page vẫn render để người dùng có thể chọn/ghép lại POS.
            _logger.LogWarning(
                "Terminal resolution failed; continuing without terminal context. TraceId={TraceId}; Path={Path}; ExceptionType={ExceptionType}",
                context.TraceIdentifier,
                context.Request.Path,
                ex.GetType().Name);
        }

        await next(context);
    }
}
