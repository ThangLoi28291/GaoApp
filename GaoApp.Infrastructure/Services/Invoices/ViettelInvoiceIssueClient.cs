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

public class ViettelInvoiceIssueClient : IViettelInvoiceIssueClient
{
    private readonly HttpClient _httpClient;
    private readonly IInvoiceIntegrationLogRepository _logRepository;
    private readonly ILogger<ViettelInvoiceIssueClient> _logger;

    public ViettelInvoiceIssueClient(
        HttpClient httpClient,
        IInvoiceIntegrationLogRepository logRepository,
        ILogger<ViettelInvoiceIssueClient>? logger = null)
    {
        _httpClient = httpClient;
        _logRepository = logRepository;
        _logger = logger ?? NullLogger<ViettelInvoiceIssueClient>.Instance;
    }

    public async Task<Result<ViettelInvoiceIssueResultDto>> IssueInvoiceAsync(
        int invoiceHeadId,
        string baseUrl,
        string username,
        string password,
        InvoiceProviderAuthMode authMode,
        string supplierTaxCode,
        ViettelInvoicePayloadDto payload,
        CancellationToken ct = default)
    {
        if (invoiceHeadId <= 0)
        {
            return Result<ViettelInvoiceIssueResultDto>.Failure(
                Error.Validation(
                    "Viettel.InvoiceHeadIdInvalid",
                    "InvoiceHeadId không hợp lệ."));
        }

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return Result<ViettelInvoiceIssueResultDto>.Failure(
                Error.Validation(
                    "Viettel.BaseUrlRequired",
                    "BaseUrl Viettel không được trống."));
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            return Result<ViettelInvoiceIssueResultDto>.Failure(
                Error.Validation(
                    "Viettel.UsernameRequired",
                    "Username Viettel không được trống."));
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            return Result<ViettelInvoiceIssueResultDto>.Failure(
                Error.Validation(
                    "Viettel.PasswordRequired",
                    "Password Viettel không được trống."));
        }

        if (string.IsNullOrWhiteSpace(supplierTaxCode))
        {
            return Result<ViettelInvoiceIssueResultDto>.Failure(
                Error.Validation(
                    "Viettel.SupplierTaxCodeRequired",
                    "MST phát hành không được trống."));
        }

        if (authMode != InvoiceProviderAuthMode.BasicAuth)
        {
            return Result<ViettelInvoiceIssueResultDto>.Failure(
                Error.Validation(
                    "Viettel.AuthModeUnsupported",
                    "Phát hành hiện dùng Basic Auth theo tài khoản Viettel của bạn. Vui lòng chọn Basic Auth."));
        }

        var normalizedBaseUrl = ViettelClientHelper.NormalizeBaseUrl(baseUrl);

        var url =
            $"{normalizedBaseUrl}/InvoiceAPI/InvoiceWS/createInvoice/{Uri.EscapeDataString(supplierTaxCode.Trim())}";

        var startedAtUtc = DateTime.UtcNow;
        var sw = Stopwatch.StartNew();

        var requestBodyJson = ViettelClientHelper.SerializeJson(payload);

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

            var responseText = await response.Content.ReadAsStringAsync(ct);

            sw.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var failDto = new ViettelInvoiceIssueResultDto
                {
                    InvoiceHeadId = invoiceHeadId,
                    IsSuccess = false,
                    ErrorCode = $"HTTP_{(int)response.StatusCode}",
                    ErrorMessage = $"Viettel phát hành thất bại. HTTP {(int)response.StatusCode}.",
                    RawResponse = string.Empty,
                    DurationMs = sw.ElapsedMilliseconds
                };

                await WriteIssueLogAsync(
                    invoiceHeadId: invoiceHeadId,
                    requestUrl: url,
                    requestBody: requestBodyJson,
                    responseBody: responseText,
                    isSuccess: false,
                    errorCode: failDto.ErrorCode,
                    errorMessage: failDto.ErrorMessage,
                    startedAtUtc: startedAtUtc,
                    durationMs: sw.ElapsedMilliseconds,
                    ct: ct);

                return Result<ViettelInvoiceIssueResultDto>.Failure(
                    Error.Validation(
                        failDto.ErrorCode,
                        failDto.ErrorMessage));
            }

            var parsed = ParseIssueResponse(
                invoiceHeadId,
                responseText,
                sw.ElapsedMilliseconds);

            await WriteIssueLogAsync(
                invoiceHeadId: invoiceHeadId,
                requestUrl: url,
                requestBody: requestBodyJson,
                responseBody: responseText,
                isSuccess: parsed.IsSuccess,
                errorCode: parsed.ErrorCode,
                errorMessage: parsed.ErrorMessage,
                startedAtUtc: startedAtUtc,
                durationMs: sw.ElapsedMilliseconds,
                ct: ct);

            return parsed.IsSuccess
                ? Result<ViettelInvoiceIssueResultDto>.Success(parsed)
                : Result<ViettelInvoiceIssueResultDto>.Failure(
                    Error.Validation(
                        parsed.ErrorCode ?? "Viettel.IssueFailed",
                        parsed.ErrorMessage ?? "Viettel trả lỗi phát hành."));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            sw.Stop();

            var message = "Gọi Viettel phát hành timeout. Cần tra cứu lại bằng transactionUuid trước khi bấm phát hành lại.";

            await WriteIssueLogAsync(
                invoiceHeadId: invoiceHeadId,
                requestUrl: url,
                requestBody: requestBodyJson,
                responseBody: null,
                isSuccess: false,
                errorCode: "TIMEOUT",
                errorMessage: message,
                startedAtUtc: startedAtUtc,
                durationMs: sw.ElapsedMilliseconds,
                ct: ct);

            return Result<ViettelInvoiceIssueResultDto>.Failure(
                Error.Validation("Viettel.IssueTimeout", message));
        }
        catch (Exception ex)
        {
            sw.Stop();

            var exceptionType = ViettelClientHelper.ExceptionType(ex);
            const string message =
                "Không gọi được dịch vụ phát hành Viettel. Cần tra cứu lại bằng transactionUuid trước khi thử lại.";

            await WriteIssueLogAsync(
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

            return Result<ViettelInvoiceIssueResultDto>.Failure(
                Error.Validation("Viettel.IssueTransportFailed", message));
        }
    }

    private static ViettelInvoiceIssueResultDto ParseIssueResponse(
        int invoiceHeadId,
        string responseText,
        long durationMs)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return new ViettelInvoiceIssueResultDto
            {
                InvoiceHeadId = invoiceHeadId,
                IsSuccess = false,
                ErrorCode = "Viettel.IssueInvalidResponse",
                ErrorMessage = "Viettel không trả dữ liệu.",
                RawResponse = string.Empty,
                DurationMs = durationMs
            };
        }

        try
        {
            var jsonValidation =
                ViettelClientHelper.TryParseStrictProviderJson(
                    responseText,
                    out var document);

            if (jsonValidation != ProviderJsonValidationFailure.None ||
                document is null)
            {
                return new ViettelInvoiceIssueResultDto
                {
                    InvoiceHeadId = invoiceHeadId,
                    IsSuccess = false,
                    ErrorCode = "Viettel.IssueInvalidResponse",
                    ErrorMessage = "Không đọc được phản hồi phát hành Viettel.",
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
                return new ViettelInvoiceIssueResultDto
                {
                    InvoiceHeadId = invoiceHeadId,
                    IsSuccess = false,
                    ErrorCode = "Viettel.IssueInvalidResponse",
                    ErrorMessage = "Viettel trả về envelope phát hành không hợp lệ.",
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
                return new ViettelInvoiceIssueResultDto
                {
                    InvoiceHeadId = invoiceHeadId,
                    IsSuccess = false,
                    ErrorCode = "Viettel.IssueBusinessFailed",
                    ErrorMessage = "Viettel trả lỗi phát hành.",
                    RawResponse = string.Empty,
                    DurationMs = durationMs
                };
            }

            var candidates = containers
                .Where(
                    container =>
                        HasIssueConfirmation(container.Element))
                .ToList();

            if (candidates.Count > 1)
            {
                return new ViettelInvoiceIssueResultDto
                {
                    InvoiceHeadId = invoiceHeadId,
                    IsSuccess = false,
                    ErrorCode = "Viettel.IssueConflictingResponse",
                    ErrorMessage =
                        "Viettel trả về phản hồi mâu thuẫn hoặc không đủ điều kiện xác nhận.",
                    RawResponse = string.Empty,
                    DurationMs = durationMs
                };
            }

            if (candidates.Count == 0)
            {
                return new ViettelInvoiceIssueResultDto
                {
                    InvoiceHeadId = invoiceHeadId,
                    IsSuccess = false,
                    ErrorCode = "Viettel.IssueAmbiguousResponse",
                    ErrorMessage =
                        "Viettel trả về phản hồi chưa đủ thông tin xác nhận phát hành. " +
                        "Cần tra cứu lại theo transactionUuid trước khi thử lại.",
                    RawResponse = string.Empty,
                    DurationMs = durationMs
                };
            }

            var payload = candidates[0].Element;
            var invoiceNo =
                ViettelClientHelper.GetFirstDirectString(
                    payload,
                    "invoiceNo",
                    "invoiceNumber");

            var transactionId =
                ViettelClientHelper.GetFirstDirectString(
                    payload,
                    "transactionID",
                    "transactionId",
                    "transactionIDStr",
                    "invoiceId");

            var reservationCode =
                ViettelClientHelper.GetFirstDirectString(
                    payload,
                    "reservationCode");

            var codeOfTax =
                ViettelClientHelper.GetFirstDirectString(
                    payload,
                    "codeOfTax");

            return new ViettelInvoiceIssueResultDto
            {
                InvoiceHeadId = invoiceHeadId,
                IsSuccess = true,
                InvoiceNo = invoiceNo,
                TransactionId = transactionId,
                ReservationCode = reservationCode,
                CodeOfTax = codeOfTax,
                ErrorCode = null,
                ErrorMessage = null,
                RawResponse = string.Empty,
                DurationMs = durationMs
            };
        }
        catch
        {
            return new ViettelInvoiceIssueResultDto
            {
                InvoiceHeadId = invoiceHeadId,
                IsSuccess = false,
                ErrorCode = "Viettel.IssueInvalidResponse",
                ErrorMessage = "Không đọc được phản hồi phát hành Viettel.",
                RawResponse = string.Empty,
                DurationMs = durationMs
            };
        }
    }

    private static bool HasIssueConfirmation(JsonElement container)
    {
        return !string.IsNullOrWhiteSpace(
                   ViettelClientHelper.GetFirstDirectString(
                       container,
                       "invoiceNo",
                       "invoiceNumber")) ||
               !string.IsNullOrWhiteSpace(
                   ViettelClientHelper.GetFirstDirectString(
                       container,
                       "transactionID",
                       "transactionId",
                       "transactionIDStr",
                       "invoiceId")) ||
               !string.IsNullOrWhiteSpace(
                   ViettelClientHelper.GetDirectString(
                       container,
                       "reservationCode")) ||
               !string.IsNullOrWhiteSpace(
                   ViettelClientHelper.GetDirectString(
                       container,
                       "codeOfTax"));
    }

    private async Task WriteIssueLogAsync(
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
                ActionType = InvoiceIntegrationActionType.IssueInvoice,
                RequestUrl = "Viettel:IssueInvoice",
                RequestBody = ViettelClientHelper.RedactedRequestSummary("IssueInvoice"),
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
            // Không để lỗi ghi log làm hỏng kết quả phát hành.
            _logger.LogWarning(
                "Invoice integration-log persistence failed; provider result is preserved. Operation={Operation}; InvoiceHeadId={InvoiceHeadId}; ExceptionType={ExceptionType}",
                "IssueInvoice",
                invoiceHeadId,
                ex.GetType().Name);
        }
    }
}
