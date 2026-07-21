using GaoApp.Application.DTOs.Invoices;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

public partial class InvoiceController
{
    private void FlashSuccess(string message)
    {
        TempData["Success"] = message;
    }

    private void FlashError(string? message, string fallback = "Có lỗi xảy ra.")
    {
        TempData["Error"] = SafeMessage(message, fallback);
    }

    private static string SafeMessage(string? message, string fallback)
    {
        return string.IsNullOrWhiteSpace(message)
            ? fallback
            : message;
    }

    private IActionResult RedirectToInvoiceDetailPage(int id)
    {
        return RedirectToAction(nameof(Detail), new { id });
    }

    private IActionResult RedirectToViettelPayloadPage(int id)
    {
        return RedirectToAction(nameof(ViettelPayload), new { id });
    }

    private BadRequestObjectResult AjaxBadRequest(
        string? message,
        string fallback)
    {
        return BadRequest(new
        {
            success = false,
            message = SafeMessage(message, fallback)
        });
    }

    private NotFoundObjectResult AjaxNotFound(
        string? message,
        string fallback)
    {
        return NotFound(new
        {
            success = false,
            message = SafeMessage(message, fallback)
        });
    }

    private JsonResult AjaxInvoiceChanged(
        string message,
        InvoiceHeadDto invoice)
    {
        return Json(new
        {
            success = true,
            message,
            invoiceHeadId = invoice.Id
        });
    }

    private JsonResult AjaxInvoiceLockChanged(
        string message,
        InvoiceHeadDto invoice)
    {
        return Json(new
        {
            success = true,
            message,
            invoiceHeadId = invoice.Id,
            isLocked = invoice.IsLocked
        });
    }

    private JsonResult AjaxBuyerLookup(
        InvoiceBuyerLookupDto buyer)
    {
        return Json(new
        {
            success = true,
            isFound = buyer.IsFound,
            buyerType = buyer.BuyerType,
            buyerName = buyer.BuyerName,
            buyerLegalName = buyer.BuyerLegalName,
            buyerTaxCode = buyer.BuyerTaxCode,
            buyerAddress = buyer.BuyerAddress,
            buyerEmail = buyer.BuyerEmail,
            buyerPhone = buyer.BuyerPhone,
            source = buyer.Source
        });
    }

    private JsonResult AjaxBuyerUpdated(
        InvoiceHeadDto invoice)
    {
        return Json(new
        {
            success = true,
            message = "Đã cập nhật thông tin người mua.",
            invoiceHeadId = invoice.Id,
            buyerType = invoice.BuyerType,
            buyerName = invoice.BuyerName,
            buyerLegalName = invoice.BuyerLegalName,
            buyerTaxCode = invoice.BuyerTaxCode,
            buyerAddress = invoice.BuyerAddress,
            buyerEmail = invoice.BuyerEmail,
            buyerPhone = invoice.BuyerPhone
        });
    }

    private JsonResult AjaxProductVariantSearch(
        IEnumerable<InvoiceProductVariantSearchItemDto> items)
    {
        return Json(items.Select(x => new
        {
            id = x.ProductVariantId,
            text = x.DisplayName,
            sku = x.Sku,
            barcode = x.Barcode,
            unitName = x.UnitName,
            unitPrice = x.UnitPrice
        }));
    }

    private FileContentResult InlineFile(
        byte[] fileBytes,
        string contentType,
        string fileName)
    {
        Response.Headers["Content-Disposition"] =
            $"inline; filename=\"{fileName}\"";

        return File(fileBytes, contentType);
    }
    private IActionResult RedirectToInvoiceIndexPage()
    {
        return RedirectToAction(nameof(Index));
    }
}