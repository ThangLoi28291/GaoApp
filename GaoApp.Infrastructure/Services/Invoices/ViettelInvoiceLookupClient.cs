using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace GaoApp.Infrastructure.Services.Invoices;

public class ViettelInvoiceLookupClient : IViettelInvoiceLookupClient
{
    private readonly HttpClient _httpClient;
    private readonly IInvoiceIntegrationLogRepository _logRepository;

    public ViettelInvoiceLookupClient(
        HttpClient httpClient,
        IInvoiceIntegrationLogRepository logRepository)
    {
        _httpClient = httpClient;
        _logRepository = logRepository;
    }

    public async Task<Result<ViettelInvoiceLookupResultDto>> SearchByTransactionUuidAsync(
        int invoiceHeadId,
        string baseUrl,
        string username,
        string password,
        InvoiceProviderAuthMode authMode,
        string supplierTaxCode,
        string transactionUuid,
        CancellationToken ct = default)
    {
        if (invoiceHeadId <= 0)
        {
            return Result<ViettelInvoiceLookupResultDto>.Failure(
                Error.Validation(
                    "Viettel.InvoiceHeadIdInvalid",
                    "InvoiceHeadId không hợp lệ."));
        }

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return Result<ViettelInvoiceLookupResultDto>.Failure(
                Error.Validation(
                    "Viettel.BaseUrlRequired",
                    "BaseUrl Viettel không được trống."));
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            return Result<ViettelInvoiceLookupResultDto>.Failure(
                Error.Validation(
                    "Viettel.UsernameRequired",
                    "Username Viettel không được trống."));
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            return Result<ViettelInvoiceLookupResultDto>.Failure(
                Error.Validation(
                    "Viettel.PasswordRequired",
                    "Password Viettel không được trống."));
        }

        if (string.IsNullOrWhiteSpace(supplierTaxCode))
        {
            return Result<ViettelInvoiceLookupResultDto>.Failure(
                Error.Validation(
                    "Viettel.SupplierTaxCodeRequired",
                    "MST phát hành không được trống."));
        }

        if (string.IsNullOrWhiteSpace(transactionUuid))
        {
            return Result<ViettelInvoiceLookupResultDto>.Failure(
                Error.Validation(
                    "Viettel.TransactionUuidRequired",
                    "TransactionUuid không được trống."));
        }

        if (authMode != InvoiceProviderAuthMode.BasicAuth)
        {
            return Result<ViettelInvoiceLookupResultDto>.Failure(
                Error.Validation(
                    "Viettel.AuthModeUnsupported",
                    "Tra cứu UUID hiện đang dùng Basic Auth theo tài khoản Viettel của bạn."));
        }

        var normalizedBaseUrl = ViettelClientHelper.NormalizeBaseUrl(baseUrl);

        var url =
            $"{normalizedBaseUrl}/InvoiceAPI/InvoiceWS/searchInvoiceByTransactionUuid";

        var form = new Dictionary<string, string>
        {
            ["supplierTaxCode"] = supplierTaxCode.Trim(),
            ["transactionUuid"] = transactionUuid.Trim()
        };

        var requestBodyForLog = string.Join(
            "&",
            form.Select(x => $"{x.Key}={x.Value}"));

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

            request.Content = new FormUrlEncodedContent(form);

            using var response = await _httpClient.SendAsync(request, ct);

            var raw = await response.Content.ReadAsStringAsync(ct);

            sw.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var fail = new ViettelInvoiceLookupResultDto
                {
                    InvoiceHeadId = invoiceHeadId,
                    IsFound = false,
                    TransactionUuid = transactionUuid,
                    ErrorCode = $"HTTP_{(int)response.StatusCode}",
                    ErrorMessage = $"Tra cứu Viettel thất bại. HTTP {(int)response.StatusCode}.",
                    RawResponse = ViettelClientHelper.Trim(raw, 10000),
                    DurationMs = sw.ElapsedMilliseconds
                };

                await WriteLookupLogAsync(
                    invoiceHeadId: invoiceHeadId,
                    requestUrl: url,
                    requestBody: requestBodyForLog,
                    responseBody: raw,
                    isSuccess: false,
                    errorCode: fail.ErrorCode,
                    errorMessage: fail.ErrorMessage,
                    startedAtUtc: startedAtUtc,
                    durationMs: sw.ElapsedMilliseconds,
                    ct: ct);

                return Result<ViettelInvoiceLookupResultDto>.Success(fail);
            }

            var parsed = ParseLookupResponse(
                invoiceHeadId,
                transactionUuid,
                raw,
                sw.ElapsedMilliseconds);

            await WriteLookupLogAsync(
                invoiceHeadId: invoiceHeadId,
                requestUrl: url,
                requestBody: requestBodyForLog,
                responseBody: raw,
                isSuccess: parsed.IsFound,
                errorCode: parsed.ErrorCode,
                errorMessage: parsed.ErrorMessage,
                startedAtUtc: startedAtUtc,
                durationMs: sw.ElapsedMilliseconds,
                ct: ct);

            return Result<ViettelInvoiceLookupResultDto>.Success(parsed);
        }
        catch (TaskCanceledException)
        {
            sw.Stop();

            var message = "Tra cứu Viettel theo UUID timeout.";

            await WriteLookupLogAsync(
                invoiceHeadId: invoiceHeadId,
                requestUrl: url,
                requestBody: requestBodyForLog,
                responseBody: null,
                isSuccess: false,
                errorCode: "TIMEOUT",
                errorMessage: message,
                startedAtUtc: startedAtUtc,
                durationMs: sw.ElapsedMilliseconds,
                ct: CancellationToken.None);

            return Result<ViettelInvoiceLookupResultDto>.Success(
                new ViettelInvoiceLookupResultDto
                {
                    InvoiceHeadId = invoiceHeadId,
                    IsFound = false,
                    TransactionUuid = transactionUuid,
                    ErrorCode = "TIMEOUT",
                    ErrorMessage = message,
                    DurationMs = sw.ElapsedMilliseconds
                });
        }
        catch (Exception ex)
        {
            sw.Stop();

            await WriteLookupLogAsync(
                invoiceHeadId: invoiceHeadId,
                requestUrl: url,
                requestBody: requestBodyForLog,
                responseBody: null,
                isSuccess: false,
                errorCode: "EXCEPTION",
                errorMessage: ex.Message,
                startedAtUtc: startedAtUtc,
                durationMs: sw.ElapsedMilliseconds,
                ct: CancellationToken.None);

            return Result<ViettelInvoiceLookupResultDto>.Success(
                new ViettelInvoiceLookupResultDto
                {
                    InvoiceHeadId = invoiceHeadId,
                    IsFound = false,
                    TransactionUuid = transactionUuid,
                    ErrorCode = "EXCEPTION",
                    ErrorMessage = ex.Message,
                    DurationMs = sw.ElapsedMilliseconds
                });
        }
    }

    private static ViettelInvoiceLookupResultDto ParseLookupResponse(
        int invoiceHeadId,
        string transactionUuid,
        string raw,
        long durationMs)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new ViettelInvoiceLookupResultDto
            {
                InvoiceHeadId = invoiceHeadId,
                IsFound = false,
                TransactionUuid = transactionUuid,
                ErrorCode = "EMPTY_RESPONSE",
                ErrorMessage = "Viettel không trả dữ liệu.",
                RawResponse = string.Empty,
                DurationMs = durationMs
            };
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            var message =
                ViettelClientHelper.FindStringProperty(root, "message") ??
                ViettelClientHelper.FindStringProperty(root, "description") ??
                ViettelClientHelper.FindStringProperty(root, "data");

            var errorCode =
                ViettelClientHelper.FindStringProperty(root, "errorCode") ??
                ViettelClientHelper.FindStringProperty(root, "code");

            var invoiceNo =
                ViettelClientHelper.FindStringProperty(root, "invoiceNo") ??
                ViettelClientHelper.FindStringProperty(root, "invoiceNumber") ??
                ViettelClientHelper.FindStringProperty(root, "supplierInvoiceNo");

            var transactionId =
                ViettelClientHelper.FindStringProperty(root, "transactionID") ??
                ViettelClientHelper.FindStringProperty(root, "transactionId") ??
                ViettelClientHelper.FindStringProperty(root, "transactionIDStr") ??
                ViettelClientHelper.FindStringProperty(root, "invoiceId");

            var reservationCode =
                ViettelClientHelper.FindStringProperty(root, "reservationCode") ??
                ViettelClientHelper.FindStringProperty(root, "reservationNo");

            var codeOfTax =
                ViettelClientHelper.FindStringProperty(root, "codeOfTax");

            var issueDateText =
                ViettelClientHelper.FindStringProperty(root, "issueDateStr") ??
                ViettelClientHelper.FindStringProperty(root, "issueDate") ??
                ViettelClientHelper.FindStringProperty(root, "invoiceIssuedDate");

            var issueDateUtc = TryParseIssueDateUtc(issueDateText);

            if (!string.IsNullOrWhiteSpace(invoiceNo))
            {
                return new ViettelInvoiceLookupResultDto
                {
                    InvoiceHeadId = invoiceHeadId,
                    IsFound = true,
                    TransactionUuid = transactionUuid,
                    InvoiceNo = invoiceNo,
                    TransactionId = transactionId,
                    ReservationCode = reservationCode,
                    CodeOfTax = codeOfTax,
                    IssueDateUtc = issueDateUtc,
                    RawResponse = ViettelClientHelper.Trim(raw, 10000),
                    DurationMs = durationMs
                };
            }

            var normalizedMessage = message ?? string.Empty;

            if (normalizedMessage.Contains("NOT_FOUND_DATA", StringComparison.OrdinalIgnoreCase) ||
                normalizedMessage.Contains("Không tìm thấy", StringComparison.OrdinalIgnoreCase))
            {
                return new ViettelInvoiceLookupResultDto
                {
                    InvoiceHeadId = invoiceHeadId,
                    IsFound = false,
                    TransactionUuid = transactionUuid,
                    ErrorCode = "NOT_FOUND_DATA",
                    ErrorMessage = "Không tìm thấy hóa đơn trên Viettel theo transactionUuid.",
                    RawResponse = ViettelClientHelper.Trim(raw, 10000),
                    DurationMs = durationMs
                };
            }

            return new ViettelInvoiceLookupResultDto
            {
                InvoiceHeadId = invoiceHeadId,
                IsFound = false,
                TransactionUuid = transactionUuid,
                ErrorCode = string.IsNullOrWhiteSpace(errorCode)
                    ? "NOT_FOUND_DATA"
                    : errorCode,
                ErrorMessage = string.IsNullOrWhiteSpace(message)
                    ? "Không tìm thấy hóa đơn trên Viettel theo transactionUuid."
                    : message,
                RawResponse = ViettelClientHelper.Trim(raw, 10000),
                DurationMs = durationMs
            };
        }
        catch (Exception ex)
        {
            return new ViettelInvoiceLookupResultDto
            {
                InvoiceHeadId = invoiceHeadId,
                IsFound = false,
                TransactionUuid = transactionUuid,
                ErrorCode = "PARSE_ERROR",
                ErrorMessage = $"Không đọc được response tra cứu UUID: {ex.Message}",
                RawResponse = ViettelClientHelper.Trim(raw, 10000),
                DurationMs = durationMs
            };
        }
    }

    private async Task WriteLookupLogAsync(
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
                ActionType = InvoiceIntegrationActionType.SearchByTransactionUuid,
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
            // Không để lỗi ghi log làm hỏng tra cứu.
        }
    }

    private static DateTime? TryParseIssueDateUtc(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();

        if (long.TryParse(value, out var millis))
        {
            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(millis).UtcDateTime;
            }
            catch
            {
                return null;
            }
        }

        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dto))
        {
            return dto.UtcDateTime;
        }

        return null;
    }
}