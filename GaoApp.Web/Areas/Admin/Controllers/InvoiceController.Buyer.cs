using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Invoices;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

public partial class InvoiceController
{
    [HttpGet]
    public async Task<IActionResult> LookupBuyerByTaxCode(
        int invoiceHeadId,
        string buyerType,
        string taxCode,
        CancellationToken ct)
    {
        var result = await _invoiceService.LookupBuyerByTaxCodeAsync(
            invoiceHeadId,
            buyerType,
            taxCode,
            ct);

        if (!result.IsSuccess)
        {
            return AjaxNotFound(
      InvoiceErrorReadSanitizer.UserMessage(result.Error),
      "Không tìm thấy thông tin người mua trong nội bộ.");
        }

        return AjaxBuyerLookup(result.Value);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateBuyerInfo(
        UpdateInvoiceBuyerInfoRequest request,
        CancellationToken ct)
    {
        var result = await _invoiceService.UpdateBuyerInfoAsync(
            request,
            ct);

        if (!result.IsSuccess)
        {
            return AjaxBadRequest(
       InvoiceErrorReadSanitizer.UserMessage(result.Error),
       "Không cập nhật được thông tin người mua.");
        }

        return AjaxBuyerUpdated(result.Value);
    }
}