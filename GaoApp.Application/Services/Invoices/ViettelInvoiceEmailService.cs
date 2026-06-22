using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Domain.Enums;
using System.Net.Mail;

namespace GaoApp.Application.Services.Invoices;

public class ViettelInvoiceEmailService : IViettelInvoiceEmailService
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IInvoiceProviderSettingRepository _settingRepository;
    private readonly IViettelInvoiceEmailClient _emailClient;

    public ViettelInvoiceEmailService(
        IInvoiceRepository invoiceRepository,
        IInvoiceProviderSettingRepository settingRepository,
        IViettelInvoiceEmailClient emailClient)
    {
        _invoiceRepository = invoiceRepository;
        _settingRepository = settingRepository;
        _emailClient = emailClient;
    }

    public async Task<Result<ViettelInvoiceSendEmailResultDto>> SendEmailAsync(
        ViettelInvoiceSendEmailRequestDto request,
        CancellationToken ct = default)
    {
        if (request.InvoiceHeadId <= 0)
        {
            return Result<ViettelInvoiceSendEmailResultDto>.Failure(
                Error.Validation(
                    "Invoice.InvalidInvoiceHeadId",
                    "InvoiceHeadId không hợp lệ."));
        }

        var buyerEmail = NormalizeEmails(request.BuyerEmail);

        if (string.IsNullOrWhiteSpace(buyerEmail))
        {
            return Result<ViettelInvoiceSendEmailResultDto>.Failure(
                Error.Validation(
                    "Invoice.BuyerEmailRequired",
                    "Vui lòng nhập email người nhận."));
        }

        var emailValidate = ValidateEmails(buyerEmail);

        if (!emailValidate.IsSuccess)
        {
            return Result<ViettelInvoiceSendEmailResultDto>.Failure(
                emailValidate.Error!);
        }

        var invoice = await _invoiceRepository.GetInvoiceHeadWithDetailsByIdAsync(
            request.InvoiceHeadId,
            ct);

        if (invoice == null)
        {
            return Result<ViettelInvoiceSendEmailResultDto>.Failure(
                Error.NotFound("Không tìm thấy hóa đơn bán ra."));
        }

        if (!IsIssuedLike(invoice))
        {
            return Result<ViettelInvoiceSendEmailResultDto>.Failure(
                Error.Validation(
                    "Invoice.NotIssued",
                    "Chỉ được gửi email sau khi hóa đơn đã phát hành Viettel."));
        }

        if (string.IsNullOrWhiteSpace(invoice.TransactionUuid))
        {
            return Result<ViettelInvoiceSendEmailResultDto>.Failure(
                Error.Validation(
                    "Invoice.TransactionUuidMissing",
                    "Hóa đơn chưa có TransactionUuid, không thể gửi email Viettel."));
        }

        var setting = await _settingRepository.GetActiveViettelAsync(
            invoice.StoreId,
            ct);

        if (setting == null)
        {
            return Result<ViettelInvoiceSendEmailResultDto>.Failure(
                Error.Validation(
                    "InvoiceProvider.NotConfigured",
                    "Chưa có cấu hình Viettel đang dùng."));
        }

        var supplierTaxCode = FirstNonEmpty(
            invoice.SupplierTaxCode,
            setting.SupplierTaxCode);

        if (string.IsNullOrWhiteSpace(supplierTaxCode))
        {
            return Result<ViettelInvoiceSendEmailResultDto>.Failure(
                Error.Validation(
                    "Invoice.SupplierTaxCodeMissing",
                    "Hóa đơn thiếu MST phát hành, không thể gửi email Viettel."));
        }

        var sent = await _emailClient.SendEmailToCustomerAsync(
            invoiceHeadId: invoice.Id,
            baseUrl: setting.BaseUrl,
            username: setting.Username,
            password: setting.Password,
            authMode: setting.AuthMode,
            supplierTaxCode: supplierTaxCode,
            transactionUuid: invoice.TransactionUuid,
            buyerEmail: buyerEmail,
            providerInvoiceNo: invoice.ProviderInvoiceNo,
            ct: ct);

        if (!sent.IsSuccess)
        {
            invoice.EmailStatus = InvoiceEmailSendStatus.Failed;
            invoice.LastEmailTo = buyerEmail;
            invoice.LastEmailErrorMessage = sent.Error?.Message;

            invoice.LastErrorCode = sent.Error?.Code;
            invoice.LastErrorMessage = sent.Error?.Message;
            invoice.LastSyncedAtUtc = DateTime.UtcNow;

            await _invoiceRepository.SaveChangesAsync(ct);

            return sent;
        }

        invoice.EmailStatus = InvoiceEmailSendStatus.Sent;
        invoice.EmailSentAtUtc = DateTime.UtcNow;
        invoice.LastEmailTo = buyerEmail;
        invoice.EmailSendCount += 1;
        invoice.LastEmailErrorMessage = null;

        // Sau Phase 21.9:
        // ProviderStatus không nên đổi sang EmailSent nữa.
        // EmailStatus đã phản ánh trạng thái gửi email riêng.

        invoice.LastSyncedAtUtc = DateTime.UtcNow;
        invoice.LastErrorCode = null;
        invoice.LastErrorMessage = null;

        await _invoiceRepository.SaveChangesAsync(ct);

        return sent;
    }

    private static bool IsIssuedLike(GaoApp.Domain.Entities.InvoiceHead invoice)
    {
        if (!string.IsNullOrWhiteSpace(invoice.ProviderInvoiceNo))
            return true;

        return invoice.ProviderStatus is
            InvoiceProviderStatus.Issued or
            InvoiceProviderStatus.PdfDownloaded or
            InvoiceProviderStatus.ZipDownloaded or
            InvoiceProviderStatus.EmailSent;
    }

    private static string NormalizeEmails(string? value)
    {
        value ??= string.Empty;

        var parts = value
            .Split(new[] { ';', ',', ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return string.Join(";", parts);
    }

    private static Result<bool> ValidateEmails(string normalizedEmails)
    {
        var emails = normalizedEmails
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        if (!emails.Any())
        {
            return Result<bool>.Failure(
                Error.Validation(
                    "Invoice.BuyerEmailRequired",
                    "Vui lòng nhập email người nhận."));
        }

        foreach (var email in emails)
        {
            try
            {
                _ = new MailAddress(email);
            }
            catch
            {
                return Result<bool>.Failure(
                    Error.Validation(
                        "Invoice.BuyerEmailInvalid",
                        $"Email không đúng định dạng: {email}"));
            }
        }

        return Result<bool>.Success(true);
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
}