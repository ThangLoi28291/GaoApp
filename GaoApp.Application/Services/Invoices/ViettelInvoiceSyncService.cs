using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Invoices;

public class ViettelInvoiceSyncService : IViettelInvoiceSyncService
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IInvoiceProviderSettingRepository _settingRepository;
    private readonly IViettelInvoiceLookupClient _lookupClient;

    public ViettelInvoiceSyncService(
        IInvoiceRepository invoiceRepository,
        IInvoiceProviderSettingRepository settingRepository,
        IViettelInvoiceLookupClient lookupClient)
    {
        _invoiceRepository = invoiceRepository;
        _settingRepository = settingRepository;
        _lookupClient = lookupClient;
    }

    public async Task<Result<ViettelInvoiceLookupResultDto>> SyncByTransactionUuidAsync(
        int invoiceHeadId,
        CancellationToken ct = default)
    {
        if (invoiceHeadId <= 0)
        {
            return Result<ViettelInvoiceLookupResultDto>.Failure(
                Error.Validation("Invoice.InvalidInvoiceHeadId", "InvoiceHeadId không hợp lệ."));
        }

        var invoice = await _invoiceRepository.GetInvoiceHeadWithDetailsByIdAsync(
            invoiceHeadId,
            ct);

        if (invoice == null)
        {
            return Result<ViettelInvoiceLookupResultDto>.Failure(
                Error.NotFound("Không tìm thấy hóa đơn bán ra."));
        }

        if (string.IsNullOrWhiteSpace(invoice.TransactionUuid))
        {
            return Result<ViettelInvoiceLookupResultDto>.Failure(
                Error.Validation(
                    "Invoice.TransactionUuidMissing",
                    "Hóa đơn chưa có TransactionUuid, không thể tra cứu Viettel."));
        }

        var setting = await _settingRepository.GetActiveViettelAsync(ct);

        if (setting == null)
        {
            return Result<ViettelInvoiceLookupResultDto>.Failure(
                Error.Validation("InvoiceProvider.NotConfigured", "Chưa có cấu hình Viettel đang dùng."));
        }

        var lookup = await _lookupClient.SearchByTransactionUuidAsync(
            invoiceHeadId: invoiceHeadId,
            baseUrl: setting.BaseUrl,
            username: setting.Username,
            password: setting.Password,
            authMode: setting.AuthMode,
            supplierTaxCode: setting.SupplierTaxCode,
            transactionUuid: invoice.TransactionUuid,
            ct: ct);

        if (!lookup.IsSuccess)
        {
            invoice.LastErrorCode = lookup.Error?.Code;
            invoice.LastErrorMessage = lookup.Error?.Message;
            invoice.LastSyncedAtUtc = DateTime.UtcNow;

            await _invoiceRepository.SaveChangesAsync(ct);

            return lookup;
        }

        var result = lookup.Value;

        if (result.IsFound)
        {
            SyncFoundInvoice(invoice, result);

            await _invoiceRepository.SaveChangesAsync(ct);

            return Result<ViettelInvoiceLookupResultDto>.Success(result);
        }

        SyncNotFoundInvoice(invoice, result);

        await _invoiceRepository.SaveChangesAsync(ct);

        return Result<ViettelInvoiceLookupResultDto>.Success(result);
    }

    private static void SyncFoundInvoice(
        GaoApp.Domain.Entities.InvoiceHead invoice,
        ViettelInvoiceLookupResultDto result)
    {
        invoice.ProviderStatus = InvoiceProviderStatus.Issued;

        if (!string.IsNullOrWhiteSpace(result.InvoiceNo))
        {
            invoice.ProviderInvoiceNo = result.InvoiceNo;
            invoice.InvoiceNumber = result.InvoiceNo;
        }

        if (!string.IsNullOrWhiteSpace(result.TransactionId))
        {
            invoice.ProviderTransactionId = result.TransactionId;
        }

        if (!string.IsNullOrWhiteSpace(result.ReservationCode))
        {
            invoice.ReservationCode = result.ReservationCode;
        }

        if (!string.IsNullOrWhiteSpace(result.CodeOfTax))
        {
            invoice.CodeOfTax = result.CodeOfTax;
        }

        if (result.IssueDateUtc.HasValue)
        {
            invoice.IssuedAtUtc = result.IssueDateUtc.Value;
        }
        else
        {
            invoice.IssuedAtUtc ??= DateTime.UtcNow;
        }

        invoice.LastSyncedAtUtc = DateTime.UtcNow;
        invoice.LastErrorCode = null;
        invoice.LastErrorMessage = null;

        invoice.IsLocked = true;
        invoice.LockedAtUtc ??= DateTime.UtcNow;

        invoice.LockReason = string.IsNullOrWhiteSpace(result.InvoiceNo)
            ? "Đã đồng bộ hóa đơn Viettel theo transactionUuid."
            : $"Đã đồng bộ hóa đơn Viettel theo transactionUuid. Số hóa đơn: {result.InvoiceNo}.";
    }

    private static void SyncNotFoundInvoice(
        GaoApp.Domain.Entities.InvoiceHead invoice,
        ViettelInvoiceLookupResultDto result)
    {
        // Nếu hóa đơn đã có số phát hành trong GaoApp thì không hạ trạng thái xuống ReadyToIssue.
        // Chỉ ghi lỗi đồng bộ để quản trị kiểm tra.
        if (invoice.ProviderStatus == InvoiceProviderStatus.Issued ||
            !string.IsNullOrWhiteSpace(invoice.ProviderInvoiceNo))
        {
            invoice.LastErrorCode = result.ErrorCode ?? "LOOKUP_NOT_FOUND";
            invoice.LastErrorMessage =
                "Tra cứu UUID không thấy trên Viettel, nhưng GaoApp đang có số hóa đơn. Cần kiểm tra đối soát.";
            invoice.LastSyncedAtUtc = DateTime.UtcNow;
            return;
        }

        invoice.ProviderStatus = InvoiceProviderStatus.ReadyToIssue;
        invoice.LastErrorCode = result.ErrorCode ?? "NOT_FOUND_DATA";
        invoice.LastErrorMessage =
            result.ErrorMessage ??
            "Không tìm thấy hóa đơn trên Viettel theo transactionUuid. Có thể phát hành lại bằng UUID cũ.";
        invoice.LastSyncedAtUtc = DateTime.UtcNow;
    }
}