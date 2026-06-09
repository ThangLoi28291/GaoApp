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

    public TerminalResolutionMiddleware(
        IPOSTerminalRepository terminalRepository,
        ICurrentStore currentStore,
        AppDbContext db)
    {
        _terminalRepository = terminalRepository;
        _currentStore = currentStore;
        _db = db;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // Reset context items cho mỗi request.
        context.Items["CurrentStoreId"] = null;
        context.Items["CurrentStoreName"] = null;
        context.Items["CurrentTerminalId"] = null;
        context.Items["CurrentTerminalName"] = null;
        context.Items["CurrentTerminalCode"] = null;

        try
        {
            var storeId = _currentStore.StoreId;

            if (storeId > 0)
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
        catch
        {
            // Không chặn request nếu resolve terminal lỗi.
            // Login page vẫn render để người dùng có thể chọn/ghép lại POS.
        }

        await next(context);
    }
}