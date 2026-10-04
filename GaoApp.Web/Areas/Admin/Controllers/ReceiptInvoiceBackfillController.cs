using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin"), ApiController, Authorize, AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("admin/api/receipt-invoice-backfill")]
public sealed class ReceiptInvoiceBackfillController(IReceiptInvoiceBackfillService service,
    IAuthorizationService authorization) : ControllerBase
{
    private async Task<(bool Direct, bool PurchaseOrder)> PermissionsAsync() =>
        ((await authorization.AuthorizeAsync(User, PermissionCodes.Inventory.StockDocument.Approve)).Succeeded,
         (await authorization.AuthorizeAsync(User, PermissionCodes.Purchase.Receipt.Approve)).Succeeded);

    [HttpGet]
    public async Task<IActionResult> Preview([FromQuery] ReceiptInvoiceBackfillQuery query, CancellationToken ct)
    {
        var permissions = await PermissionsAsync();
        if (!permissions.Direct && !permissions.PurchaseOrder) return Forbid();
        try { return Ok(await service.PreviewAsync(query, permissions.Direct, permissions.PurchaseOrder, ct)); }
        catch (BusinessRuleException error) { return BadRequest(new { message = error.SafeMessage }); }
    }

    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm(ReceiptInvoiceBackfillRequest request, CancellationToken ct)
    {
        var permissions = await PermissionsAsync();
        if (!permissions.Direct && !permissions.PurchaseOrder) return Forbid();
        try { return Ok(await service.ConfirmAsync(request, permissions.Direct, permissions.PurchaseOrder, ct)); }
        catch (BusinessRuleException error) { return Conflict(new { message = error.SafeMessage }); }
    }
}
