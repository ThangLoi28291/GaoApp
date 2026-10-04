using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.DTOs.Inventory.InputInvoices;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Domain.Enums;
using GaoApp.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin"), ApiController, Authorize, AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("admin/api/stock-documents/{id:int}/pricing/xml-mappings")]
public sealed class PurchaseReceiptXmlMappingController(
    IInputInvoiceItemCatalogMappingService service,
    IInputInvoiceRepository repository,
    ITenantContext tenant,
    IAuthorizationService authorization) : ControllerBase
{
    [HttpPost]
    [RequireAnyPermission(PermissionCodes.Purchase.Receipt.Approve,
        PermissionCodes.Inventory.StockDocument.Approve)]
    public async Task<IActionResult> Confirm(int id,
        [FromBody] ConfirmInputInvoicePricingMappingRequest request,
        CancellationToken ct)
    {
        if (tenant.StoreId is not > 0)
            return BadRequest(new { message = "Không xác định được cửa hàng hiện tại." });
        var receipt = await repository.GetReceiptForSupplierResolutionAsync(
            tenant.StoreId.Value, id, ct);
        if (receipt is null)
            return NotFound(new { message = "Không tìm thấy phiếu nhập kho." });
        var policy = receipt.ReceiptSource == PurchaseReceiptSource.PurchaseOrder
            ? PermissionCodes.Purchase.Receipt.Approve
            : PermissionCodes.Inventory.StockDocument.Approve;
        if (!(await authorization.AuthorizeAsync(User, null, policy)).Succeeded)
            return Forbid();

        try
        {
            return Ok(await service.ConfirmForPricingAsync(
                tenant.StoreId.Value, id, request, ct));
        }
        catch (PurchaseReceiptPricingConflictException error)
        {
            return Conflict(new { message = error.SafeMessage });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "Phiếu hoặc mapping vừa thay đổi. Vui lòng tải lại." });
        }
        catch (DbUpdateException error) when (error.InnerException is
            Microsoft.Data.SqlClient.SqlException sql && sql.Number is 2601 or 2627 or 1205)
        {
            return Conflict(new { message = "Mapping vừa được cập nhật ở nơi khác. Vui lòng tải lại." });
        }
        catch (Microsoft.Data.SqlClient.SqlException error) when (error.Number == 1205)
        {
            return Conflict(new { message = "Có cập nhật đồng thời. Vui lòng tải lại và thử lại." });
        }
        catch (BusinessRuleException error)
        {
            return BadRequest(new { message = error.SafeMessage });
        }
    }
}
