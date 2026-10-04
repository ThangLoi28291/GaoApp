using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Security;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin"), Authorize, ApiController]
[Route("admin/api/stock-documents/{documentId:int}/selling-price-reviews")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ReceiptPriceReviewsController(IReceiptSellingPriceService service, IAuthorizationService authorization) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(int documentId, CancellationToken ct)
    {
        try
        {
            var data = await service.GetReviewsAsync(documentId, ct);
            var po = data.ReceiptSource == PurchaseReceiptSource.PurchaseOrder;
            var approve = await Has(po ? PermissionCodes.Purchase.Receipt.Approve : PermissionCodes.Inventory.StockDocument.Approve);
            if (!approve && !await Has(po ? PermissionCodes.Purchase.Receipt.View : PermissionCodes.Inventory.StockDocument.View) &&
                !await Has(po ? PermissionCodes.Purchase.Receipt.Update : PermissionCodes.Inventory.StockDocument.Update)) return Forbid();
            var cost = approve || await Has(PermissionCodes.Purchase.Order.ViewCost);
            return Ok(data with { Lines = data.Lines.Select(x => cost ? x : x with { ReviewedBaseCost = null, CurrentBaseCost = null }).ToList() });
        }
        catch (BusinessRuleException ex) { return Conflict(new { message = ex.SafeMessage }); }
    }
    private async Task<bool> Has(string permission) => (await authorization.AuthorizeAsync(User, permission)).Succeeded;
}
