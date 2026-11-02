using GaoApp.Application.Common.Security;
using GaoApp.Application.Interfaces.Services.Invoices;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

public partial class InvoiceController
{
    [HttpGet]
    public async Task<IActionResult> SearchOrdersForDraft(string? term,
        [FromServices] IInvoiceOrderDraftService service, CancellationToken ct)
    {
        var result = await service.SearchAsync(term, ct);
        return result.IsSuccess ? Json(result.Value) : AjaxBadRequest(InvoiceErrorReadSanitizer.UserMessage(result.Error), "Không tìm được đơn POS.");
    }

    [HttpGet]
    public async Task<IActionResult> PreviewOrderDraft(int orderId,
        [FromServices] IInvoiceOrderDraftService service, CancellationToken ct)
    {
        var result = await service.PreviewAsync(orderId, ct);
        return result.IsSuccess ? Json(result.Value) : AjaxBadRequest(InvoiceErrorReadSanitizer.UserMessage(result.Error), "Không tìm được đơn POS.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateOrderDraft(int orderId,
        [FromServices] IInvoiceOrderDraftService service, CancellationToken ct)
    {
        var result = await service.CreateAsync(orderId, ct);
        return result.IsSuccess ? Json(result.Value) : AjaxBadRequest(InvoiceErrorReadSanitizer.UserMessage(result.Error), "Không tạo được hóa đơn.");
    }
}
