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

namespace GaoApp.Infrastructure.Services.Invoices;

public class ViettelInvoiceIssueClient : IViettelInvoiceIssueClient
{
    private readonly HttpClient _httpClient;
    private readonly IInvoiceIntegrationLogRepository _logRepository;

    public ViettelInvoiceIssueClient(
        HttpClient httpClient,
        IInvoiceIntegrationLogRepository logRepository)
    {
        _httpClient = httpClient;
        _logRepository = logRepository;
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
                    RawResponse = ViettelClientHelper.Trim(responseText, 10000),
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

                return Result<ViettelInvoiceIssueResultDto>.Success(failDto);
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

            return Result<ViettelInvoiceIssueResultDto>.Success(parsed);
        }
        catch (TaskCanceledException)
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
                ct: CancellationToken.None);

            return Result<ViettelInvoiceIssueResultDto>.Success(
                new ViettelInvoiceIssueResultDto
                {
                    InvoiceHeadId = invoiceHeadId,
                    IsSuccess = false,
                    ErrorCode = "TIMEOUT",
                    ErrorMessage = message,
                    DurationMs = sw.ElapsedMilliseconds
                });
        }
        catch (Exception ex)
        {
            sw.Stop();

            await WriteIssueLogAsync(
                invoiceHeadId: invoiceHeadId,
                requestUrl: url,
                requestBody: requestBodyJson,
                responseBody: null,
                isSuccess: false,
                errorCode: "EXCEPTION",
                errorMessage: ex.Message,
                startedAtUtc: startedAtUtc,
                durationMs: sw.ElapsedMilliseconds,
                ct: CancellationToken.None);

            return Result<ViettelInvoiceIssueResultDto>.Success(
                new ViettelInvoiceIssueResultDto
                {
                    InvoiceHeadId = invoiceHeadId,
                    IsSuccess = false,
                    ErrorCode = "EXCEPTION",
                    ErrorMessage = ex.Message,
                    DurationMs = sw.ElapsedMilliseconds
                });
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
                ErrorCode = "EMPTY_RESPONSE",
                ErrorMessage = "Viettel không trả dữ liệu.",
                RawResponse = string.Empty,
                DurationMs = durationMs
            };
        }

        try
        {
            using var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;

            var errorCode =
                ViettelClientHelper.FindStringProperty(root, "errorCode") ??
                ViettelClientHelper.FindStringProperty(root, "code");

            var description =
                ViettelClientHelper.FindStringProperty(root, "description") ??
                ViettelClientHelper.FindStringProperty(root, "message");

            var isError = !ViettelClientHelper.IsViettelSuccessCode(errorCode);

            if (isError)
            {
                return new ViettelInvoiceIssueResultDto
                {
                    InvoiceHeadId = invoiceHeadId,
                    IsSuccess = false,
                    ErrorCode = errorCode,
                    ErrorMessage = string.IsNullOrWhiteSpace(description)
                        ? "Viettel trả lỗi phát hành."
                        : description,
                    RawResponse = ViettelClientHelper.Trim(responseText, 10000),
                    DurationMs = durationMs
                };
            }

            var invoiceNo =
                ViettelClientHelper.FindStringProperty(root, "invoiceNo") ??
                ViettelClientHelper.FindStringProperty(root, "invoiceNumber");

            var transactionId =
                ViettelClientHelper.FindStringProperty(root, "transactionID") ??
                ViettelClientHelper.FindStringProperty(root, "transactionId") ??
                ViettelClientHelper.FindStringProperty(root, "transactionIDStr") ??
                ViettelClientHelper.FindStringProperty(root, "invoiceId");

            var reservationCode =
                ViettelClientHelper.FindStringProperty(root, "reservationCode");

            var codeOfTax =
                ViettelClientHelper.FindStringProperty(root, "codeOfTax");

            var success =
                !string.IsNullOrWhiteSpace(invoiceNo) ||
                !string.IsNullOrWhiteSpace(transactionId) ||
                ViettelClientHelper.IsViettelSuccessCode(errorCode);

            return new ViettelInvoiceIssueResultDto
            {
                InvoiceHeadId = invoiceHeadId,
                IsSuccess = success,
                InvoiceNo = invoiceNo,
                TransactionId = transactionId,
                ReservationCode = reservationCode,
                CodeOfTax = codeOfTax,
                ErrorCode = success ? null : errorCode,
                ErrorMessage = success ? null : description,
                RawResponse = ViettelClientHelper.Trim(responseText, 10000),
                DurationMs = durationMs
            };
        }
        catch (Exception ex)
        {
            return new ViettelInvoiceIssueResultDto
            {
                InvoiceHeadId = invoiceHeadId,
                IsSuccess = false,
                ErrorCode = "PARSE_ERROR",
                ErrorMessage = $"Không đọc được response Viettel: {ex.Message}",
                RawResponse = ViettelClientHelper.Trim(responseText, 10000),
                DurationMs = durationMs
            };
        }
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
                RequestUrl = ViettelClientHelper.TrimNullable(requestUrl, 500),
                RequestBody = ViettelClientHelper.TrimNullable(requestBody, 10000),
                ResponseBody = ViettelClientHelper.TrimNullable(responseBody, 10000),
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
        catch
        {
            // Không để lỗi ghi log làm hỏng kết quả phát hành.
        }
    }
}