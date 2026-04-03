using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Interfaces.Repositories.POSTerminals;

namespace GaoApp.Web.Middlewares;

public class TerminalResolutionMiddleware : IMiddleware
{
    private readonly IPOSTerminalRepository _terminalRepository;
    private readonly ICurrentStore _currentStore;
    private readonly IClientNetworkInfo _clientNetworkInfo;

    public TerminalResolutionMiddleware(
        IPOSTerminalRepository terminalRepository,
        ICurrentStore currentStore,
        IClientNetworkInfo clientNetworkInfo)
    {
        _terminalRepository = terminalRepository;
        _currentStore = currentStore;
        _clientNetworkInfo = clientNetworkInfo;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // reset để tránh request sau giữ dữ liệu cũ
        context.Items["CurrentTerminalId"] = null;
        context.Items["CurrentTerminalName"] = null;
        context.Items["CurrentTerminalCode"] = null;

        try
        {
            var storeId = _currentStore.StoreId;

            // chỉ resolve terminal khi đang đứng trong store cụ thể
            if (storeId > 0)
            {
                var clientIp = _clientNetworkInfo.GetClientIp();

                if (!string.IsNullOrWhiteSpace(clientIp))
                {
                    var terminal = await _terminalRepository.GetByStoreAndIpAsync(
                        storeId,
                        clientIp,
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
            // Không chặn request nếu resolve terminal lỗi
            // Login page / page POS vẫn có thể render,
            // chỉ là phần terminal có thể chưa hiện.
        }

        await next(context);
    }
}