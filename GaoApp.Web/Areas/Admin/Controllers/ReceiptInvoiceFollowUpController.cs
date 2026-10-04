using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin"), ApiController, Authorize, AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("admin/api/stock-documents/{id:int}/invoice-follow-up")]
public sealed class ReceiptInvoiceFollowUpController(IReceiptInvoiceFollowUpService service,
    IAuthorizationService authorization) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> Get(int id, CancellationToken ct) => Execute(id, ct);

    [HttpPost("end-waiting")]
    public Task<IActionResult> EndWaiting(int id, EndReceiptInvoiceWaitRequest request, CancellationToken ct)
        => Execute(id, ct, () => service.EndWaitingAsync(id, request, ct));

    [HttpPost("review")]
    public Task<IActionResult> Review(int id, ReviewReceiptInvoiceRequest request, CancellationToken ct)
        => Execute(id, ct, () => service.ReviewAsync(id, request, ct));

    private async Task<IActionResult> Execute(int id, CancellationToken ct, Func<Task>? mutate = null)
    {
        try
        {
            var context = await service.GetAsync(id, ct);
            var policy = context.ReceiptSource == PurchaseReceiptSource.PurchaseOrder
                ? PermissionCodes.Purchase.Receipt.Approve : PermissionCodes.Inventory.StockDocument.Approve;
            if (!(await authorization.AuthorizeAsync(User, policy)).Succeeded) return Forbid();
            if (mutate != null) await mutate();
            return Ok(mutate != null ? await service.GetAsync(id, ct) : context);
        }
        catch (BusinessRuleException error) { return Conflict(new { message = error.SafeMessage }); }
    }
}
