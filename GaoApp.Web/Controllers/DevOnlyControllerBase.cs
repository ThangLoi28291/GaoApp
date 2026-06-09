using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace GaoApp.Web.Controllers;

/// <summary>
/// Base controller chỉ cho phép chạy trong môi trường Development.
/// Dùng cho các endpoint test/dev nội bộ, tránh lộ ra production.
/// </summary>
[ApiExplorerSettings(IgnoreApi = true)]
public abstract class DevOnlyControllerBase : Controller
{
    /// <summary>
    /// Trả về NotFound nếu môi trường hiện tại không phải Development.
    /// </summary>
    protected IActionResult? EnsureDevelopmentOnly()
    {
        var env = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();

        if (!env.IsDevelopment())
        {
            return NotFound();
        }

        return null;
    }
}