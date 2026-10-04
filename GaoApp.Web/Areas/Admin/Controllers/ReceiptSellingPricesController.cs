using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin"), ApiController, AutoValidateAntiforgeryToken]
[Authorize(Policy = PermissionCodes.Catalog.Product.Update)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("admin/api/stock-documents/{documentId:int}/lines/{lineId:int}/selling-prices")]
public sealed class ReceiptSellingPricesController(IReceiptSellingPriceService service,
    IAuthorizationService authorization) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> Get(int documentId, int lineId, CancellationToken ct) => Call(async () =>
    {
        var data = await service.GetAsync(documentId, lineId, ct);
        return await Allowed(data) ? Ok(data) : Forbid();
    });

    [HttpPost]
    public Task<IActionResult> Update(int documentId, int lineId,
        UpdateReceiptSellingPricesRequest request, CancellationToken ct) => Call(async () =>
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        if (!await Allowed(await service.GetAsync(documentId, lineId, ct))) return Forbid();
        return Ok(await service.UpdateAsync(documentId, lineId, request, ct));
    });

    private async Task<bool> Allowed(ReceiptSellingPriceDto data) =>
        (await authorization.AuthorizeAsync(User, data.ReceiptSource == PurchaseReceiptSource.PurchaseOrder
            ? PermissionCodes.Purchase.Receipt.Approve : PermissionCodes.Inventory.StockDocument.Approve)).Succeeded;
    private static async Task<IActionResult> Call(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (BusinessRuleException ex) { return new ConflictObjectResult(new { message = ex.SafeMessage }); }
    }
}
