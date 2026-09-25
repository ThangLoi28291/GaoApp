using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using System.Security.Cryptography;

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

        InvoiceProviderSetting? setting;
        try
        {
            setting = await _settingRepository.GetForInvoiceAsync(
                invoice.StoreId,
                invoice.InvoiceProviderSettingId,
                ct);
        }
        catch (CryptographicException ex)
        {
            try
            {
                var current = await _settingRepository.GetForInvoiceAsync(
                    invoice.StoreId,
                    null,
                    ct);
                if (current == null || !MatchesInvoiceIdentity(invoice, current))
                    return await FailCredentialReadAsync(invoice, ex, current?.Id, ct);

                setting = current;
            }
            catch (CryptographicException currentException)
            {
                return await FailCredentialReadAsync(invoice, currentException, null, ct, ex);
            }
        }

        if (setting == null)
        {
            return Result<ViettelInvoiceLookupResultDto>.Failure(
                Error.Validation("InvoiceProvider.NotConfigured", "Chưa có cấu hình Viettel đang dùng."));
        }
        var supplierTaxCode = FirstNonEmpty(
    invoice.SupplierTaxCode,
    setting.SupplierTaxCode);

        if (string.IsNullOrWhiteSpace(supplierTaxCode))
        {
            return Result<ViettelInvoiceLookupResultDto>.Failure(
                Error.Validation(
                    "Invoice.SupplierTaxCodeMissing",
                    "Hóa đơn thiếu MST phát hành, không thể tra cứu Viettel."));
        }
        var lookup = await _lookupClient.SearchByTransactionUuidAsync(
            invoiceHeadId: invoiceHeadId,
            baseUrl: setting.BaseUrl,
            username: setting.Username,
            password: setting.Password,
            authMode: setting.AuthMode,
           supplierTaxCode: supplierTaxCode,
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
    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }

    private async Task<Result<ViettelInvoiceLookupResultDto>> FailCredentialReadAsync(
        InvoiceHead invoice,
        CryptographicException exception,
        int? currentSettingId,
        CancellationToken ct,
        CryptographicException? historicalException = null)
    {
        const string code = "InvoiceProvider.CredentialKeyUnavailable";
        var message =
            "Không đọc được mật khẩu Viettel của cấu hình hóa đơn. " +
            $"InvoiceProviderSettingId={invoice.InvoiceProviderSettingId?.ToString() ?? "null"}; " +
            $"cấu hình hiện hành={currentSettingId?.ToString() ?? "không có"}. " +
            $"Chi tiết: {exception.Message}" +
            (historicalException == null
                ? string.Empty
                : $" Cấu hình cũ: {historicalException.Message}") +
            " Hãy bảo đảm Web và Worker dùng chung DataProtection:KeysPath và lưu lại cấu hình Viettel.";
        invoice.LastErrorCode = code;
        invoice.LastErrorMessage = message;
        invoice.LastSyncedAtUtc = DateTime.UtcNow;
        await _invoiceRepository.SaveChangesAsync(ct);
        return Result<ViettelInvoiceLookupResultDto>.Failure(Error.Validation(code, message));
    }

    private static bool MatchesInvoiceIdentity(
        InvoiceHead invoice,
        InvoiceProviderSetting setting)
    {
        return Matches(invoice.ProviderCode, setting.ProviderCode) &&
               Matches(invoice.SupplierTaxCode, setting.SupplierTaxCode) &&
               Matches(invoice.InvoiceType, setting.InvoiceType) &&
               Matches(invoice.TemplateCode, setting.TemplateCode) &&
               Matches(invoice.InvoiceSeries, setting.InvoiceSeries);
    }

    private static bool Matches(string? snapshot, string current)
        => string.IsNullOrWhiteSpace(snapshot) ||
           string.Equals(snapshot.Trim(), current.Trim(), StringComparison.OrdinalIgnoreCase);
}
