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
        var invoice = await _invoiceRepository.GetInvoiceHeadWithDetailsByIdAsync(invoiceHeadId, ct);

        if (invoice == null)
        {
            return Result<ViettelOfficialFileResultDto>.Failure(
                Error.NotFound("Không tìm thấy hóa đơn bán ra."));
        }

        if (invoice.ProviderStatus != InvoiceProviderStatus.Issued &&
            string.IsNullOrWhiteSpace(invoice.ProviderInvoiceNo))
        {
            return Result<ViettelOfficialFileResultDto>.Failure(
                Error.Validation(
                    "Invoice.NotIssued",
                    "Hóa đơn chưa phát hành Viettel, chưa thể tải file chính thức."));
        }

        var invoiceNo = !string.IsNullOrWhiteSpace(invoice.ProviderInvoiceNo)
            ? invoice.ProviderInvoiceNo
            : invoice.InvoiceNumber;

        if (string.IsNullOrWhiteSpace(invoiceNo))
        {
            return Result<ViettelOfficialFileResultDto>.Failure(
                Error.Validation("Invoice.InvoiceNoMissing", "Hóa đơn chưa có số hóa đơn Viettel."));
        }

        if (!invoice.IssuedAtUtc.HasValue)
        {
            return Result<ViettelOfficialFileResultDto>.Failure(
                Error.Validation("Invoice.IssuedAtMissing", "Hóa đơn chưa có thời điểm phát hành."));
        }

        var setting = await _settingRepository.GetActiveViettelAsync(ct);

        if (setting == null)
        {
            return Result<ViettelOfficialFileResultDto>.Failure(
                Error.Validation("InvoiceProvider.NotConfigured", "Chưa có cấu hình Viettel đang dùng."));
        }

        var templateCode = !string.IsNullOrWhiteSpace(invoice.TemplateCode)
      ? invoice.TemplateCode
      : setting.TemplateCode;

        var invoiceSeries = !string.IsNullOrWhiteSpace(invoice.InvoiceSeries)
            ? invoice.InvoiceSeries
            : setting.InvoiceSeries;

        var download = await _client.DownloadOfficialFileAsync(
            invoiceHeadId: invoiceHeadId,
            fileType: fileType,
            baseUrl: setting.BaseUrl,
            username: setting.Username,
            password: setting.Password,
            authMode: setting.AuthMode,
            supplierTaxCode: setting.SupplierTaxCode,
            invoiceNo: invoiceNo,
            templateCode: templateCode,
            invoiceSeries: invoiceSeries,
            issuedAtUtc: invoice.IssuedAtUtc.Value,
            ct: ct);

        if (!download.IsSuccess)
            return download;

        var file = download.Value;

        var now = DateTime.Now;
        var folder = $"invoices/viettel/{now:yyyy}/{now:MM}";
        var safeInvoiceNo = invoiceNo.Replace("/", "-").Replace("\\", "-");

        var fileName = fileType == ViettelOfficialFileType.Pdf
            ? $"{safeInvoiceNo}.pdf"
            : $"{safeInvoiceNo}.zip";

        var storedPath = await _storage.SaveAsync(
            folder,
            fileName,
            file.FileBytes,
            ct);

        file.StoredPath = storedPath;
        file.FileName = fileName;

        if (fileType == ViettelOfficialFileType.Pdf)
        {
            invoice.PdfFilePath = storedPath;
            invoice.ProviderStatus = InvoiceProviderStatus.PdfDownloaded;
        }
        else
        {
            invoice.ZipFilePath = storedPath;
            invoice.ProviderStatus = InvoiceProviderStatus.ZipDownloaded;
        }

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
        var invoice = await _invoiceRepository.GetInvoiceHeadWithDetailsByIdAsync(invoiceHeadId, ct);

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
}