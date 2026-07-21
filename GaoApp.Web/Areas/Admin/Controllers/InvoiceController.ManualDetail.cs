using GaoApp.Application.DTOs.Invoices;
using GaoApp.Web.Areas.Admin.ViewModels.Invoices;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

public partial class InvoiceController
{
    [HttpGet]
    public IActionResult CreateManualDetail(int invoiceHeadId)
    {
        var vm = new CreateManualInvoiceDetailViewModel
        {
            InvoiceHeadId = invoiceHeadId,
            Quantity = 1,
            VatRate = 0
        };

        return PartialView("_CreateManualDetailModal", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateManualDetail(
        CreateManualInvoiceDetailViewModel vm,
        CancellationToken ct)
    {
        var result = await _invoiceService.AddManualDetailAsync(
            new CreateManualInvoiceDetailRequest
            {
                InvoiceHeadId = vm.InvoiceHeadId,
                ProductVariantId = vm.ProductVariantId,
                ItemName = vm.ItemName,
                UnitName = vm.UnitName,
                Quantity = vm.Quantity,
                UnitPrice = vm.UnitPrice,
                VatRate = vm.VatRate,
                Note = vm.Note
            },
            ct);

        if (!result.IsSuccess)
        {
            return AjaxBadRequest(
      result.Error?.Message,
      "Không thêm được dòng manual.");
        }

        return AjaxInvoiceChanged(
     "Đã thêm dòng manual.",
     result.Value);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteManualDetail(
        int invoiceDetailId,
        CancellationToken ct)
    {
        var result = await _invoiceService.DeleteManualDetailAsync(
            invoiceDetailId,
            ct);

        if (!result.IsSuccess)
        {
            return AjaxBadRequest(
     result.Error?.Message,
     "Không xóa được dòng manual.");
        }

        return AjaxInvoiceChanged(
        "Đã xóa dòng manual.",
        result.Value);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateManualDetail(
        UpdateManualInvoiceDetailRequest request,
        CancellationToken ct)
    {
        var result = await _invoiceService.UpdateManualDetailAsync(
            request,
            ct);

        if (!result.IsSuccess)
        {
            return AjaxBadRequest(
     result.Error?.Message,
     "Không cập nhật được dòng manual.");
        }

        return AjaxInvoiceChanged(
      "Đã cập nhật dòng manual.",
      result.Value);
    }
}