using GaoApp.Web.Services.Assistant;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.ViewComponents;

public sealed class AssistantViewComponent(AssistantAccess access, IOptionsMonitor<AssistantOptions> options,
    ILogger<AssistantViewComponent> logger) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (!options.CurrentValue.Enabled || User.Identity?.IsAuthenticated != true) return Content("");
        try { _ = await access.CurrentAsync(null, HttpContext.RequestAborted); return View("~/Areas/Admin/Views/Shared/Components/Assistant/Default.cshtml"); }
        catch (AssistantException) { return Content(""); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogWarning("Assistant launcher unavailable: {ExceptionType}", error.GetType().Name);
            return Content("");
        }
    }
}
