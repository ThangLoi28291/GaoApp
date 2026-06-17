using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
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
                Error.Validation("Viettel.InvoiceHeadIdInvalid", "InvoiceHeadId không hợp lệ."));
        }

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return Result<ViettelInvoiceLookupResultDto>.Failure(
                Error.Validation("Viettel.BaseUrlRequired", "BaseUrl Viettel không được trống."));
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            return Result<ViettelInvoiceLookupResultDto>.Failure(
                Error.Validation("Viettel.UsernameRequired", "Username Viettel không được trống."));
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            return Result<ViettelInvoiceLookupResultDto>.Failure(
                Error.Validation("Viettel.PasswordRequired", "Password Viettel không được trống."));
        }

        if (string.IsNullOrWhiteSpace(supplierTaxCode))
        {
            return Result<ViettelInvoiceLookupResultDto>.Failure(
                Error.Validation("Viettel.SupplierTaxCodeRequired", "MST phát hành không được trống."));
        }

        if (string.IsNullOrWhiteSpace(transactionUuid))
        {
            return Result<ViettelInvoiceLookupResultDto>.Failure(
                Error.Validation("Viettel.TransactionUuidRequired", "TransactionUuid không được trống."));
        }

        if (authMode != InvoiceProviderAuthMode.BasicAuth)
        {
            return Result<ViettelInvoiceLookupResultDto>.Failure(
                Error.Validation(
                    "Viettel.AuthModeUnsupported",
                    "Tra cứu UUID hiện đang dùng Basic Auth theo tài khoản Viettel của bạn."));
        }

        var url = $"{baseUrl.TrimEnd('/')}/InvoiceAPI/InvoiceWS/searchInvoiceByTransactionUuid";

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
            request.Headers.Authorization = BuildBasicAuthHeader(username, password);

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
                    RawResponse = Trim(raw, 10000),
                    DurationMs = sw.ElapsedMilliseconds
                };

                await WriteLookupLogAsync(
                    invoiceHeadId,
                    url,
                    requestBodyForLog,
                    raw,
                    false,
                    fail.ErrorCode,
                    fail.ErrorMessage,
                    startedAtUtc,
                    sw.ElapsedMilliseconds,
                    ct);

                return Result<ViettelInvoiceLookupResultDto>.Success(fail);
            }

            var parsed = ParseLookupResponse(
                invoiceHeadId,
                transactionUuid,
                raw,
                sw.ElapsedMilliseconds);

            await WriteLookupLogAsync(
                invoiceHeadId,
                url,
                requestBodyForLog,
                raw,
                parsed.IsFound,
                parsed.ErrorCode,
                parsed.ErrorMessage,
                startedAtUtc,
                sw.ElapsedMilliseconds,
                ct);

            return Result<ViettelInvoiceLookupResultDto>.Success(parsed);
        }
        catch (TaskCanceledException)
        {
            sw.Stop();

            var message = "Tra cứu Viettel theo UUID timeout.";

            await WriteLookupLogAsync(
                invoiceHeadId,
                url,
                requestBodyForLog,
                null,
                false,
                "TIMEOUT",
                message,
                startedAtUtc,
                sw.ElapsedMilliseconds,
                CancellationToken.None);

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
                invoiceHeadId,
                url,
                requestBodyForLog,
                null,
                false,
                "EXCEPTION",
                ex.Message,
                startedAtUtc,
                sw.ElapsedMilliseconds,
                CancellationToken.None);

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
                FindStringProperty(root, "message")
                ?? FindStringProperty(root, "description")
                ?? FindStringProperty(root, "data");

            var errorCode =
                FindStringProperty(root, "errorCode")
                ?? FindStringProperty(root, "code");

            var invoiceNo =
                FindStringProperty(root, "invoiceNo")
                ?? FindStringProperty(root, "invoiceNumber")
                ?? FindStringProperty(root, "supplierInvoiceNo");

            var transactionId =
                FindStringProperty(root, "transactionID")
                ?? FindStringProperty(root, "transactionId")
                ?? FindStringProperty(root, "transactionIDStr")
                ?? FindStringProperty(root, "invoiceId");

            var reservationCode =
                FindStringProperty(root, "reservationCode")
                ?? FindStringProperty(root, "reservationNo");

            var codeOfTax =
                FindStringProperty(root, "codeOfTax");

            var issueDateText =
                FindStringProperty(root, "issueDateStr")
                ?? FindStringProperty(root, "issueDate")
                ?? FindStringProperty(root, "invoiceIssuedDate");

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
                    RawResponse = Trim(raw, 10000),
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
                    RawResponse = Trim(raw, 10000),
                    DurationMs = durationMs
                };
            }

            return new ViettelInvoiceLookupResultDto
            {
                InvoiceHeadId = invoiceHeadId,
                IsFound = false,
                TransactionUuid = transactionUuid,
                ErrorCode = string.IsNullOrWhiteSpace(errorCode) ? "NOT_FOUND_DATA" : errorCode,
                ErrorMessage = string.IsNullOrWhiteSpace(message)
                    ? "Không tìm thấy hóa đơn trên Viettel theo transactionUuid."
                    : message,
                RawResponse = Trim(raw, 10000),
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
                RawResponse = Trim(raw, 10000),
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
                RequestUrl = TrimNullable(requestUrl, 500),
                RequestBody = TrimNullable(requestBody, 10000),
                ResponseBody = TrimNullable(responseBody, 10000),
                IsSuccess = isSuccess,
                ErrorCode = TrimNullable(errorCode, 100),
                ErrorMessage = TrimNullable(errorMessage, 1000),
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

    private static AuthenticationHeaderValue BuildBasicAuthHeader(
        string username,
        string password)
    {
        var rawCredential = $"{username.Trim()}:{password}";
        var base64Credential = Convert.ToBase64String(Encoding.UTF8.GetBytes(rawCredential));

        return new AuthenticationHeaderValue("Basic", base64Credential);
    }

    private static bool TryFindProperty(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }

                if (TryFindProperty(prop.Value, propertyName, out value))
                    return true;
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryFindProperty(item, propertyName, out value))
                    return true;
            }
        }

        value = default;
        return false;
    }

    private static string? FindStringProperty(
        JsonElement element,
        string propertyName)
    {
        if (!TryFindProperty(element, propertyName, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.String)
            return value.GetString();

        if (value.ValueKind == JsonValueKind.Number)
            return value.ToString();

        if (value.ValueKind == JsonValueKind.True)
            return "true";

        if (value.ValueKind == JsonValueKind.False)
            return "false";

        if (value.ValueKind == JsonValueKind.Null)
            return null;

        return value.ToString();
    }

    private static string Trim(
        string? value,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        value = value.Trim();

        return value.Length <= maxLength
            ? value
            : value[..maxLength] + "...";
    }

    private static string? TrimNullable(
        string? value,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();

        return value.Length <= maxLength
            ? value
            : value[..maxLength] + "...";
    }
}