using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Invoices;

public class ViettelOfficialFileService : IViettelOfficialFileService
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IInvoiceProviderSettingRepository _settingRepository;
    private readonly IViettelOfficialFileClient _client;
    private readonly IInvoiceFileStorage _storage;

    public ViettelOfficialFileService(
        IInvoiceRepository invoiceRepository,
        IInvoiceProviderSettingRepository settingRepository,
        IViettelOfficialFileClient client,
        IInvoiceFileStorage storage)
    {
        _invoiceRepository = invoiceRepository;
        _settingRepository = settingRepository;
        _client = client;
        _storage = storage;
    }

    public async Task<Result<ViettelOfficialFileResultDto>> DownloadAndSaveAsync(
        int invoiceHeadId,
        ViettelOfficialFileType fileType,
        CancellationToken ct = default)
    {
        if (invoiceHeadId <= 0)
        {
            return Result<ViettelOfficialFileResultDto>.Failure(
                Error.Validation(
                    "Invoice.InvalidInvoiceHeadId",
                    "InvoiceHeadId không hợp lệ."));
        }

        var invoice = await _invoiceRepository.GetInvoiceHeadWithDetailsByIdAsync(
            invoiceHeadId,
            ct);

        if (invoice == null)
        {
            return Result<ViettelOfficialFileResultDto>.Failure(
                Error.NotFound("Không tìm thấy hóa đơn bán ra."));
        }

        if (!IsIssuedLike(invoice.ProviderStatus, invoice.ProviderInvoiceNo))
        {
            return Result<ViettelOfficialFileResultDto>.Failure(
                Error.Validation(
                    "Invoice.NotIssued",
                    "Hóa đơn chưa phát hành Viettel, chưa thể tải file chính thức."));
        }

        var invoiceNo = FirstNonEmpty(
            invoice.ProviderInvoiceNo,
            invoice.InvoiceNumber);

        if (string.IsNullOrWhiteSpace(invoiceNo))
        {
            return Result<ViettelOfficialFileResultDto>.Failure(
                Error.Validation(
                    "Invoice.InvoiceNoMissing",
                    "Hóa đơn chưa có số hóa đơn Viettel."));
        }

        if (!invoice.IssuedAtUtc.HasValue)
        {
            return Result<ViettelOfficialFileResultDto>.Failure(
                Error.Validation(
                    "Invoice.IssuedAtMissing",
                    "Hóa đơn chưa có thời điểm phát hành."));
        }

        var setting = await _settingRepository.GetForInvoiceAsync(
            invoice.StoreId,
            invoice.InvoiceProviderSettingId,
            ct);

        if (setting == null)
        {
            return Result<ViettelOfficialFileResultDto>.Failure(
                Error.Validation(
                    "InvoiceProvider.NotConfigured",
                    "Chưa có cấu hình Viettel đang dùng cho cửa hàng này."));
        }

        var supplierTaxCode = FirstNonEmpty(
            invoice.SupplierTaxCode,
            setting.SupplierTaxCode);

        var templateCode = FirstNonEmpty(
            invoice.TemplateCode,
            setting.TemplateCode);

        var invoiceSeries = FirstNonEmpty(
            invoice.InvoiceSeries,
            setting.InvoiceSeries);

        if (string.IsNullOrWhiteSpace(supplierTaxCode) ||
            string.IsNullOrWhiteSpace(templateCode) ||
            string.IsNullOrWhiteSpace(invoiceSeries))
        {
            return Result<ViettelOfficialFileResultDto>.Failure(
                Error.Validation(
                    "Invoice.ViettelSnapshotInvalid",
                    "Hóa đơn thiếu snapshot Viettel: MST phát hành, mẫu số hoặc ký hiệu."));
        }

        if (string.IsNullOrWhiteSpace(setting.BaseUrl) ||
            string.IsNullOrWhiteSpace(setting.Username) ||
            string.IsNullOrWhiteSpace(setting.Password))
        {
            return Result<ViettelOfficialFileResultDto>.Failure(
                Error.Validation(
                    "InvoiceProvider.CredentialMissing",
                    "Cấu hình Viettel thiếu BaseUrl, Username hoặc Password."));
        }

        var download = await _client.DownloadOfficialFileAsync(
            invoiceHeadId: invoiceHeadId,
            fileType: fileType,
            baseUrl: setting.BaseUrl,
            username: setting.Username,
            password: setting.Password,
            authMode: setting.AuthMode,
            supplierTaxCode: supplierTaxCode,
            invoiceNo: invoiceNo,
            templateCode: templateCode,
            invoiceSeries: invoiceSeries,
            issuedAtUtc: invoice.IssuedAtUtc.Value,
            ct: ct);

        if (!download.IsSuccess)
        {
            MarkFileDownloadFailed(
                invoice,
                fileType,
                download.Error?.Message);

            invoice.LastSyncedAtUtc = DateTime.UtcNow;
            invoice.LastErrorCode = download.Error?.Code;
            invoice.LastErrorMessage = download.Error?.Message;

            await _invoiceRepository.SaveChangesAsync(ct);

            return download;
        }

        var file = download.Value;

        if (file.FileBytes == null || file.FileBytes.Length == 0)
        {
            MarkFileDownloadFailed(
                invoice,
                fileType,
                "Viettel trả file rỗng, không thể lưu.");

            invoice.LastSyncedAtUtc = DateTime.UtcNow;
            invoice.LastErrorCode = "Invoice.FileBytesEmpty";
            invoice.LastErrorMessage = "Viettel trả file rỗng, không thể lưu.";

            await _invoiceRepository.SaveChangesAsync(ct);

            return Result<ViettelOfficialFileResultDto>.Failure(
                Error.Validation(
                    "Invoice.FileBytesEmpty",
                    "Viettel trả file rỗng, không thể lưu."));
        }

        var now = DateTime.Now;
        var folder = $"invoices/viettel/{now:yyyy}/{now:MM}";
        var safeInvoiceNo = MakeSafeFileName(invoiceNo);

        var fileName = fileType == ViettelOfficialFileType.Pdf
            ? $"{safeInvoiceNo}.pdf"
            : $"{safeInvoiceNo}.zip";

        string storedPath;

        try
        {
            storedPath = await _storage.SaveAsync(
                folder,
                fileName,
                file.FileBytes,
                ct);
        }
        catch (Exception ex)
        {
            MarkFileDownloadFailed(
                invoice,
                fileType,
                ex.Message);

            invoice.LastSyncedAtUtc = DateTime.UtcNow;
            invoice.LastErrorCode = "Invoice.FileSaveFailed";
            invoice.LastErrorMessage = ex.Message;

            await _invoiceRepository.SaveChangesAsync(ct);

            return Result<ViettelOfficialFileResultDto>.Failure(
                Error.Failure($"Lưu file hóa đơn thất bại: {ex.Message}"));
        }

        file.StoredPath = storedPath;
        file.FileName = fileName;

        if (fileType == ViettelOfficialFileType.Pdf)
        {
            invoice.PdfFilePath = storedPath;
            invoice.OfficialPdfStatus = InvoiceFileDownloadStatus.Downloaded;
            invoice.OfficialPdfDownloadedAtUtc = DateTime.UtcNow;
            invoice.OfficialPdfFileName = fileName;
        }
        else
        {
            invoice.ZipFilePath = storedPath;
            invoice.OfficialZipXmlStatus = InvoiceFileDownloadStatus.Downloaded;
            invoice.OfficialZipXmlDownloadedAtUtc = DateTime.UtcNow;
            invoice.OfficialZipXmlFileName = fileName;
        }

        // Sau Phase 21.9:
        // ProviderStatus chỉ nên phản ánh trạng thái phát hành/đồng bộ Viettel.
        // Không đổi ProviderStatus thành PdfDownloaded/ZipDownloaded nữa.

        invoice.LastSyncedAtUtc = DateTime.UtcNow;
        invoice.LastErrorCode = null;
        invoice.LastErrorMessage = null;

        await _invoiceRepository.SaveChangesAsync(ct);

        return Result<ViettelOfficialFileResultDto>.Success(file);
    }

    public async Task<Result<ViettelOfficialFileResultDto>> ReadSavedAsync(
        int invoiceHeadId,
        ViettelOfficialFileType fileType,
        CancellationToken ct = default)
    {
        if (invoiceHeadId <= 0)
        {
            return Result<ViettelOfficialFileResultDto>.Failure(
                Error.Validation(
                    "Invoice.InvalidInvoiceHeadId",
                    "InvoiceHeadId không hợp lệ."));
        }

        var invoice = await _invoiceRepository.GetInvoiceHeadWithDetailsByIdAsync(
            invoiceHeadId,
            ct);

        if (invoice == null)
        {
            return Result<ViettelOfficialFileResultDto>.Failure(
                Error.NotFound("Không tìm thấy hóa đơn bán ra."));
        }

        var path = fileType == ViettelOfficialFileType.Pdf
            ? invoice.PdfFilePath
            : invoice.ZipFilePath;

        if (string.IsNullOrWhiteSpace(path))
        {
            return Result<ViettelOfficialFileResultDto>.Failure(
                Error.NotFound("Chưa có file đã lưu."));
        }

        var stored = await _storage.ReadAsync(path, ct);

        if (stored == null)
        {
            return Result<ViettelOfficialFileResultDto>.Failure(
                Error.NotFound("File đã lưu không còn tồn tại trên máy chủ."));
        }

        return Result<ViettelOfficialFileResultDto>.Success(
            new ViettelOfficialFileResultDto
            {
                InvoiceHeadId = invoiceHeadId,
                FileType = fileType,
                FileName = stored.Value.FileName,
                ContentType = stored.Value.ContentType,
                FileBytes = stored.Value.Bytes,
                StoredPath = path
            });
    }

    private static void MarkFileDownloadFailed(
        GaoApp.Domain.Entities.InvoiceHead invoice,
        ViettelOfficialFileType fileType,
        string? errorMessage)
    {
        if (fileType == ViettelOfficialFileType.Pdf)
        {
            invoice.OfficialPdfStatus = InvoiceFileDownloadStatus.Failed;
            return;
        }

        invoice.OfficialZipXmlStatus = InvoiceFileDownloadStatus.Failed;
    }

    private static bool IsIssuedLike(
        InvoiceProviderStatus status,
        string? providerInvoiceNo)
    {
        if (!string.IsNullOrWhiteSpace(providerInvoiceNo))
            return true;

        return status is
            InvoiceProviderStatus.Issued or
            InvoiceProviderStatus.PdfDownloaded or
            InvoiceProviderStatus.ZipDownloaded or
            InvoiceProviderStatus.EmailSent;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }

    private static string MakeSafeFileName(string value)
    {
        value = (value ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(value))
            return "invoice";

        foreach (var c in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(c, '-');
        }

        value = value
            .Replace("/", "-")
            .Replace("\\", "-")
            .Replace(":", "-")
            .Trim();

        return string.IsNullOrWhiteSpace(value)
            ? "invoice"
            : value;
    }
}
