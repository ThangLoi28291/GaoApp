using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GaoApp.Storefront.Web.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class OrderCancellationController(IStorefrontCancellationService service) : Controller
{
    public sealed class AccessRequest { public string Token { get; set; } = ""; }

    [HttpPost("/shop/orders/cancellation/access"), ValidateAntiForgeryToken, EnableRateLimiting("guest-order")]
    [Consumes("application/json"), RequestSizeLimit(2048)]
    public Task<IActionResult> Access([FromBody] AccessRequest? request, CancellationToken ct)
    {
        if (request is null || !ModelState.IsValid) return Task.FromResult<IActionResult>(BadRequest(new { message = "Liên kết riêng không hợp lệ." }));
        return Execute(async () =>
        {
            var state = await service.GetGuestAsync(request.Token, ct);
            return state is null ? NotFound(new { message = "Liên kết riêng không hợp lệ hoặc không thuộc cửa hàng này." }) : Json(state);
        });
    }

    [HttpPost("/shop/orders/cancellation"), ValidateAntiForgeryToken, EnableRateLimiting("guest-order")]
    [Consumes("application/json"), RequestSizeLimit(16384)]
    public Task<IActionResult> Cancel([FromBody] StorefrontGuestCancellationRequest? request, CancellationToken ct)
    {
        if (request is null || !ModelState.IsValid) return Task.FromResult<IActionResult>(BadRequest(new { message = "Yêu cầu hủy chưa hợp lệ. Hãy kiểm tra lại thông tin." }));
        return Execute(async () => Json(await service.CancelGuestAsync(request, ct)));
    }

    private async Task<IActionResult> Execute(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (StorefrontRetryableCommandException ex) { return Conflict(new { message = ex.Message, code = "RETRY_SAME_COMMAND", retryable = true }); }
        catch (ValidationAppException ex) { return UnprocessableEntity(new { message = ex.Message }); }
        catch (ConflictAppException ex) { return Conflict(new { message = ex.Message }); }
        catch (ForbiddenAppException) { return StatusCode(403, new { message = "Không được thực hiện thao tác với đơn này." }); }
        catch (NotFoundAppException) { return NotFound(new { message = "Liên kết riêng không hợp lệ hoặc không thuộc cửa hàng này." }); }
    }
}
