using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace GaoApp.Infrastructure.Services.Invoices;

public class ViettelInvoiceEmailClient : IViettelInvoiceEmailClient
{
    private readonly HttpClient _httpClient;
    private readonly IInvoiceIntegrationLogRepository _logRepository;
    private readonly ILogger<ViettelInvoiceEmailClient> _logger;

    public ViettelInvoiceEmailClient(
        HttpClient httpClient,
        IInvoiceIntegrationLogRepository logRepository,
        ILogger<ViettelInvoiceEmailClient>? logger = null)
    {
        _httpClient = httpClient;
        _logRepository = logRepository;
        _logger = logger ?? NullLogger<ViettelInvoiceEmailClient>.Instance;
    }

    public async Task<Result<ViettelInvoiceSendEmailResultDto>> SendEmailToCustomerAsync(
        int invoiceHeadId,
        string baseUrl,
        string username,
        string password,
        InvoiceProviderAuthMode authMode,
        string supplierTaxCode,
        string transactionUuid,
        string buyerEmail,
        string? providerInvoiceNo,
        CancellationToken ct = default)
    {
        if (invoiceHeadId <= 0)
        {
            return Result<ViettelInvoiceSendEmailResultDto>.Failure(
                Error.Validation(
                    "Viettel.InvoiceHeadIdInvalid",
                    "InvoiceHeadId không hợp lệ."));
        }

        if (authMode != InvoiceProviderAuthMode.BasicAuth)
        {
            return Result<ViettelInvoiceSendEmailResultDto>.Failure(
                Error.Validation(
                    "Viettel.AuthModeUnsupported",
                    "Gửi email Viettel hiện dùng Basic Auth."));
        }

        if (string.IsNullOrWhiteSpace(baseUrl) ||
            string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password) ||
            string.IsNullOrWhiteSpace(supplierTaxCode) ||
            string.IsNullOrWhiteSpace(transactionUuid) ||
            string.IsNullOrWhiteSpace(buyerEmail))
        {
            return Result<ViettelInvoiceSendEmailResultDto>.Failure(
                Error.Validation(
                    "Viettel.RequiredMissing",
                    "Thiếu thông tin để gửi email hóa đơn Viettel."));
        }

        var normalizedEmail = NormalizeEmails(buyerEmail);

        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return Result<ViettelInvoiceSendEmailResultDto>.Failure(
                Error.Validation(
                    "Viettel.BuyerEmailRequired",
                    "Email người nhận không được trống."));
        }

        var normalizedBaseUrl = ViettelClientHelper.NormalizeBaseUrl(baseUrl);

        var url =
            $"{normalizedBaseUrl}/InvoiceAPI/InvoiceUtilsWS/sendEmailToCustomer";

        var body = new
        {
            supplierTaxCode = supplierTaxCode.Trim(),
            transactionUuid = transactionUuid.Trim(),
            buyerEmail = normalizedEmail
        };

        var requestBodyJson = ViettelClientHelper.SerializeJson(body);

        var startedAtUtc = DateTime.UtcNow;
        var sw = Stopwatch.StartNew();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);

            request.Headers.Accept.Clear();
            request.Headers.Accept.ParseAdd("application/json");

            request.Headers.Authorization = ViettelClientHelper.BuildBasicAuthHeader(
                username,
                password);

            request.Content = new StringContent(
                requestBodyJson,
                Encoding.UTF8,
                "application/json");

            using var response = await _httpClient.SendAsync(request, ct);

            var raw = await response.Content.ReadAsStringAsync(ct);

            sw.Stop();

            if (!response.IsSuccessStatusCode)
            {
                await WriteLogAsync(
                    invoiceHeadId: invoiceHeadId,
                    requestUrl: url,
                    requestBody: requestBodyJson,
                    responseBody: raw,
                    isSuccess: false,
                    errorCode: $"HTTP_{(int)response.StatusCode}",
                    errorMessage: $"Gửi email Viettel thất bại. HTTP {(int)response.StatusCode}.",
                    startedAtUtc: startedAtUtc,
                    durationMs: sw.ElapsedMilliseconds,
                    ct: ct);

                return Result<ViettelInvoiceSendEmailResultDto>.Failure(
                    Error.Validation(
                        "Viettel.SendEmailHttpFailed",
                        $"Gửi email Viettel thất bại. HTTP {(int)response.StatusCode}."));
            }

            var parsed = ParseResponse(
                invoiceHeadId,
                transactionUuid,
                normalizedEmail,
                providerInvoiceNo,
                raw,
                sw.ElapsedMilliseconds);

            await WriteLogAsync(
                invoiceHeadId: invoiceHeadId,
                requestUrl: url,
                requestBody: requestBodyJson,
                responseBody: raw,
                isSuccess: parsed.IsSuccess,
                errorCode: parsed.IsSuccess ? null : parsed.Code,
                errorMessage: parsed.IsSuccess ? null : parsed.Message,
                startedAtUtc: startedAtUtc,
                durationMs: sw.ElapsedMilliseconds,
                ct: ct);

            if (!parsed.IsSuccess)
            {
                return Result<ViettelInvoiceSendEmailResultDto>.Failure(
                    Error.Validation(
                        parsed.Code ?? "Viettel.SendEmailBusinessFailed",
                        parsed.Message ?? "Viettel không gửi được email."));
            }

            return Result<ViettelInvoiceSendEmailResultDto>.Success(parsed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            sw.Stop();

            await WriteLogAsync(
                invoiceHeadId: invoiceHeadId,
                requestUrl: url,
                requestBody: requestBodyJson,
                responseBody: null,
                isSuccess: false,
                errorCode: "TIMEOUT",
                errorMessage: "Gửi email Viettel timeout.",
                startedAtUtc: startedAtUtc,
                durationMs: sw.ElapsedMilliseconds,
                ct: ct);

            return Result<ViettelInvoiceSendEmailResultDto>.Failure(
                Error.Validation(
                    "Viettel.SendEmailTimeout",
                    "Gửi email Viettel timeout. Thử lại sau."));
        }
        catch (Exception ex)
        {
            sw.Stop();

            var exceptionType = ViettelClientHelper.ExceptionType(ex);
            const string message = "Không gọi được dịch vụ gửi email Viettel.";

            await WriteLogAsync(
                invoiceHeadId: invoiceHeadId,
                requestUrl: url,
                requestBody: requestBodyJson,
                responseBody: null,
                isSuccess: false,
                errorCode: "EXCEPTION",
                errorMessage: exceptionType,
                startedAtUtc: startedAtUtc,
                durationMs: sw.ElapsedMilliseconds,
                ct: ct);

            return Result<ViettelInvoiceSendEmailResultDto>.Failure(
                Error.Validation(
                    "Viettel.SendEmailException",
                    message));
        }
    }

    private static ViettelInvoiceSendEmailResultDto ParseResponse(
        int invoiceHeadId,
        string transactionUuid,
        string buyerEmail,
        string? providerInvoiceNo,
        string raw,
        long durationMs)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new ViettelInvoiceSendEmailResultDto
            {
                InvoiceHeadId = invoiceHeadId,
                IsSuccess = false,
                BuyerEmail = buyerEmail,
                TransactionUuid = transactionUuid,
                ProviderInvoiceNo = providerInvoiceNo,
                Code = "Viettel.SendEmailInvalidResponse",
                Message = "Viettel không trả dữ liệu.",
                RawResponse = string.Empty,
                DurationMs = durationMs
            };
        }

        try
        {
            var jsonValidation =
                ViettelClientHelper.TryParseStrictProviderJson(
                    raw,
                    out var document);

            if (jsonValidation != ProviderJsonValidationFailure.None ||
                document is null)
            {
                return new ViettelInvoiceSendEmailResultDto
                {
                    InvoiceHeadId = invoiceHeadId,
                    IsSuccess = false,
                    BuyerEmail = buyerEmail,
                    TransactionUuid = transactionUuid,
                    ProviderInvoiceNo = providerInvoiceNo,
                    Code = "Viettel.SendEmailInvalidResponse",
                    Message = "Không đọc được phản hồi gửi email Viettel.",
                    RawResponse = string.Empty,
                    DurationMs = durationMs
                };
            }

            using var strictDocument = document;
            var root = strictDocument.RootElement;
            var containers =
                ViettelClientHelper.GetDirectRecognizedContainers(
                    root,
                    allowResultArray: false,
                    allowDataArray: false,
                    out var hasInvalidEnvelope);

            if (hasInvalidEnvelope)
            {
                return new ViettelInvoiceSendEmailResultDto
                {
                    InvoiceHeadId = invoiceHeadId,
                    IsSuccess = false,
                    BuyerEmail = buyerEmail,
                    TransactionUuid = transactionUuid,
                    ProviderInvoiceNo = providerInvoiceNo,
                    Code = "Viettel.SendEmailInvalidResponse",
                    Message = "Viettel trả về envelope gửi email không hợp lệ.",
                    RawResponse = string.Empty,
                    DurationMs = durationMs
                };
            }

            if (containers.Any(
                    container =>
                        ViettelClientHelper.HasDirectFailureMarker(
                            container.Element,
                            ViettelClientHelper.IsExplicitViettelSuccessCode)))
            {
                return new ViettelInvoiceSendEmailResultDto
                {
                    InvoiceHeadId = invoiceHeadId,
                    IsSuccess = false,
                    BuyerEmail = buyerEmail,
                    TransactionUuid = transactionUuid,
                    ProviderInvoiceNo = providerInvoiceNo,
                    Code = "Viettel.SendEmailBusinessFailed",
                    Message = "Viettel trả lỗi gửi email.",
                    RawResponse = string.Empty,
                    DurationMs = durationMs
                };
            }

            var candidates = containers
                .Where(
                    container =>
                        HasEmailSuccessMarker(container.Element))
                .ToList();

            if (candidates.Count > 1)
            {
                return new ViettelInvoiceSendEmailResultDto
                {
                    InvoiceHeadId = invoiceHeadId,
                    IsSuccess = false,
                    BuyerEmail = buyerEmail,
                    TransactionUuid = transactionUuid,
                    ProviderInvoiceNo = providerInvoiceNo,
                    Code = "Viettel.SendEmailConflictingResponse",
                    Message =
                        "Viettel trả về phản hồi mâu thuẫn hoặc không đủ điều kiện xác nhận.",
                    RawResponse = string.Empty,
                    DurationMs = durationMs
                };
            }

            if (candidates.Count == 0)
            {
                return new ViettelInvoiceSendEmailResultDto
                {
                    InvoiceHeadId = invoiceHeadId,
                    IsSuccess = false,
                    BuyerEmail = buyerEmail,
                    TransactionUuid = transactionUuid,
                    ProviderInvoiceNo = providerInvoiceNo,
                    Code = "Viettel.SendEmailAmbiguousResponse",
                    Message = "Viettel trả về phản hồi chưa đủ thông tin xác nhận gửi email.",
                    RawResponse = string.Empty,
                    DurationMs = durationMs
                };
            }

            var payload = candidates[0].Element;
            var code =
                ViettelClientHelper.GetFirstDirectString(
                    payload,
                    "code",
                    "errorCode");

            return new ViettelInvoiceSendEmailResultDto
            {
                InvoiceHeadId = invoiceHeadId,
                IsSuccess = true,
                BuyerEmail = buyerEmail,
                TransactionUuid = transactionUuid,
                ProviderInvoiceNo = providerInvoiceNo,
                Code = code,
                Message = "OK",
                RawResponse = string.Empty,
                DurationMs = durationMs
            };
        }
        catch
        {
            return new ViettelInvoiceSendEmailResultDto
            {
                InvoiceHeadId = invoiceHeadId,
                IsSuccess = false,
                BuyerEmail = buyerEmail,
                TransactionUuid = transactionUuid,
                ProviderInvoiceNo = providerInvoiceNo,
                Code = "Viettel.SendEmailInvalidResponse",
                Message = "Không đọc được phản hồi gửi email Viettel.",
                RawResponse = string.Empty,
                DurationMs = durationMs
            };
        }
    }

    private static bool HasEmailSuccessMarker(JsonElement container)
    {
        var code =
            ViettelClientHelper.GetFirstDirectString(
                container,
                "code",
                "errorCode");
        var message =
            ViettelClientHelper.GetFirstDirectString(
                container,
                "message",
                "description");

        return ViettelClientHelper.IsExplicitViettelSuccessCode(code) ||
               string.Equals(
                   message,
                   "OK",
                   StringComparison.OrdinalIgnoreCase);
    }

    private async Task WriteLogAsync(
        int invoiceHeadId,
        string? requestUrl,
        string? requestBody,
        string? responseBody,
        bool isSuccess,
        string? errorCode,
        string? errorMessage,
        DateTime startedAtUtc,
        long durationMs,
        CancellationToken ct)
    {
        try
        {
            var log = new InvoiceIntegrationLog
            {
                InvoiceHeadId = invoiceHeadId,
                ActionType = InvoiceIntegrationActionType.SendEmail,
                RequestUrl = "Viettel:SendEmail",
                RequestBody = ViettelClientHelper.RedactedRequestSummary("SendEmail"),
                ResponseBody = ViettelClientHelper.RedactedResponseSummary(responseBody),
                IsSuccess = isSuccess,
                ErrorCode = ViettelClientHelper.TrimNullable(errorCode, 100),
                ErrorMessage = ViettelClientHelper.TrimNullable(errorMessage, 1000),
                StartedAtUtc = startedAtUtc,
                FinishedAtUtc = DateTime.UtcNow,
                DurationMs = durationMs
            };

            await _logRepository.AddAsync(log, ct);
            await _logRepository.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Không để lỗi ghi log làm hỏng gửi email.
            _logger.LogWarning(
                "Invoice integration-log persistence failed; provider result is preserved. Operation={Operation}; InvoiceHeadId={InvoiceHeadId}; ExceptionType={ExceptionType}",
                "SendInvoiceEmail",
                invoiceHeadId,
                ex.GetType().Name);
        }
    }

    private static string NormalizeEmails(string value)
    {
        var parts = value
            .Split(new[] { ';', ',', ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return string.Join(";", parts);
    }
}
