using GaoApp.Application.DTOs.Invoices;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

public partial class InvoiceController
{
    [HttpGet]
    public async Task<IActionResult> ViettelPayload(
        int id,
        CancellationToken ct)
    {
        var result = await _viettelPayloadBuilder.BuildAsync(id, ct);

        if (!result.IsSuccess)
        {
            FlashError(result.Error?.Message);
            return RedirectToInvoiceDetailPage(id);
        }

        await LoadCorrectionHistoryViewBagAsync(id, ct);

        return View("ViettelPayload", result.Value);
    }

    [HttpGet]
    public async Task<IActionResult> ViettelDraftPreview(
        int id,
        CancellationToken ct)
    {
        var result = await _viettelPreviewService.PreviewDraftPdfAsync(
            id,
            ct);

        if (!result.IsSuccess)
        {
            FlashError(
                result.Error?.Message,
                "Không xem trước được hóa đơn nháp Viettel.");

            return RedirectToViettelPayloadPage(id);
        }

        var file = result.Value;

        return InlineFile(
            file.FileBytes,
            file.ContentType,
            file.FileName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ViettelIssue(
        int id,
        CancellationToken ct)
    {
        var result = await _viettelIssueService.IssueAsync(
            id,
            ct);

        if (!result.IsSuccess)
        {
            FlashError(
                result.Error?.Message,
                "Phát hành hóa đơn Viettel thất bại.");

            return RedirectToViettelPayloadPage(id);
        }

        var data = result.Value;

        FlashSuccess(
            string.IsNullOrWhiteSpace(data.InvoiceNo)
                ? "Đã gửi phát hành Viettel thành công, nhưng chưa nhận được số hóa đơn. Cần tra cứu lại bằng transactionUuid."
                : $"Đã phát hành hóa đơn Viettel thành công. Số hóa đơn: {data.InvoiceNo}.");

        return RedirectToViettelPayloadPage(id);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SyncViettelByUuid(
        int id,
        CancellationToken ct)
    {
        var result = await _viettelInvoiceSyncService.SyncByTransactionUuidAsync(
            id,
            ct);

        if (!result.IsSuccess)
        {
            FlashError(
                result.Error?.Message,
                "Tra cứu Viettel theo UUID thất bại.");

            return RedirectToViettelPayloadPage(id);
        }

        var data = result.Value;

        if (data.IsFound)
        {
            FlashSuccess(
                string.IsNullOrWhiteSpace(data.InvoiceNo)
                    ? "Đã tìm thấy hóa đơn trên Viettel theo transactionUuid và đồng bộ trạng thái."
                    : $"Đã đồng bộ hóa đơn Viettel thành công. Số hóa đơn: {data.InvoiceNo}.");

            return RedirectToViettelPayloadPage(id);
        }

        FlashError(
            data.ErrorMessage,
            "Không tìm thấy hóa đơn trên Viettel theo transactionUuid. Có thể phát hành lại bằng UUID cũ.");

        return RedirectToViettelPayloadPage(id);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendViettelEmail(
        ViettelInvoiceSendEmailRequestDto request,
        CancellationToken ct)
    {
        var result = await _viettelInvoiceEmailService.SendEmailAsync(
            request,
            ct);

        if (!result.IsSuccess)
        {
            FlashError(
                result.Error?.Message,
                "Gửi email hóa đơn Viettel thất bại.");

            return RedirectToViettelPayloadPage(request.InvoiceHeadId);
        }

        FlashSuccess(
            $"Đã gửi email hóa đơn cho: {result.Value.BuyerEmail}");

        return RedirectToViettelPayloadPage(request.InvoiceHeadId);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DownloadOfficialPdf(
        int id,
        CancellationToken ct)
    {
        var result = await _viettelOfficialFileService.DownloadAndSaveAsync(
            id,
            ViettelOfficialFileType.Pdf,
            ct);

        if (!result.IsSuccess)
        {
            FlashError(
                result.Error?.Message,
                "Tải PDF chính thức thất bại.");

            return RedirectToViettelPayloadPage(id);
        }

        FlashSuccess(
            $"Đã tải và lưu PDF chính thức: {result.Value.FileName}");

        return RedirectToViettelPayloadPage(id);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DownloadOfficialZipXml(
        int id,
        CancellationToken ct)
    {
        var result = await _viettelOfficialFileService.DownloadAndSaveAsync(
            id,
            ViettelOfficialFileType.ZipXml,
            ct);

        if (!result.IsSuccess)
        {
            FlashError(
                result.Error?.Message,
                "Tải ZIP/XML chính thức thất bại.");

            return RedirectToViettelPayloadPage(id);
        }

        FlashSuccess(
            $"Đã tải và lưu ZIP/XML chính thức: {result.Value.FileName}");

        return RedirectToViettelPayloadPage(id);
    }

    [HttpGet]
    public async Task<IActionResult> ViewOfficialPdf(
        int id,
        CancellationToken ct)
    {
        var result = await _viettelOfficialFileService.ReadSavedAsync(
            id,
            ViettelOfficialFileType.Pdf,
            ct);

        if (!result.IsSuccess)
        {
            FlashError(
                result.Error?.Message,
                "Chưa có PDF chính thức.");

            return RedirectToViettelPayloadPage(id);
        }

        return InlineFile(
            result.Value.FileBytes,
            result.Value.ContentType,
            result.Value.FileName);
    }

    [HttpGet]
    public async Task<IActionResult> DownloadSavedZipXml(
        int id,
        CancellationToken ct)
    {
        var result = await _viettelOfficialFileService.ReadSavedAsync(
            id,
            ViettelOfficialFileType.ZipXml,
            ct);

        if (!result.IsSuccess)
        {
            FlashError(
                result.Error?.Message,
                "Chưa có ZIP/XML chính thức.");

            return RedirectToViettelPayloadPage(id);
        }

        return File(
            result.Value.FileBytes,
            result.Value.ContentType,
            result.Value.FileName);
    }
}