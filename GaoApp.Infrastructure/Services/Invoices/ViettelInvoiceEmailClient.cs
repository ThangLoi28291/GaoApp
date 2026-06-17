using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GaoApp.Infrastructure.Services.Invoices;

public class ViettelInvoiceEmailClient : IViettelInvoiceEmailClient
{
    private readonly HttpClient _httpClient;
    private readonly IInvoiceIntegrationLogRepository _logRepository;

    public ViettelInvoiceEmailClient(
        HttpClient httpClient,
        IInvoiceIntegrationLogRepository logRepository)
    {
        _httpClient = httpClient;
        _logRepository = logRepository;
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
                Error.Validation("Viettel.InvoiceHeadIdInvalid", "InvoiceHeadId không hợp lệ."));
        }

        if (authMode != InvoiceProviderAuthMode.BasicAuth)
        {
            return Result<ViettelInvoiceSendEmailResultDto>.Failure(
                Error.Validation("Viettel.AuthModeUnsupported", "Gửi email Viettel hiện dùng Basic Auth."));
        }

        if (string.IsNullOrWhiteSpace(baseUrl) ||
            string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password) ||
            string.IsNullOrWhiteSpace(supplierTaxCode) ||
            string.IsNullOrWhiteSpace(transactionUuid) ||
            string.IsNullOrWhiteSpace(buyerEmail))
        {
            return Result<ViettelInvoiceSendEmailResultDto>.Failure(
                Error.Validation("Viettel.RequiredMissing", "Thiếu thông tin để gửi email hóa đơn Viettel."));
        }

        var normalizedEmail = NormalizeEmails(buyerEmail);

        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return Result<ViettelInvoiceSendEmailResultDto>.Failure(
                Error.Validation("Viettel.BuyerEmailRequired", "Email người nhận không được trống."));
        }

        var url = $"{baseUrl.TrimEnd('/')}/InvoiceAPI/InvoiceUtilsWS/sendEmailToCustomer";

        var body = new
        {
            supplierTaxCode = supplierTaxCode.Trim(),
            transactionUuid = transactionUuid.Trim(),
            buyerEmail = normalizedEmail
        };

        var requestBodyJson = JsonSerializer.Serialize(
            body,
            new JsonSerializerOptions
            {
                WriteIndented = false,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

        var startedAtUtc = DateTime.UtcNow;
        var sw = Stopwatch.StartNew();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);

            request.Headers.Accept.Clear();
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.Authorization = BuildBasicAuthHeader(username, password);

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
                    invoiceHeadId,
                    url,
                    requestBodyJson,
                    raw,
                    false,
                    $"HTTP_{(int)response.StatusCode}",
                    $"Gửi email Viettel thất bại. HTTP {(int)response.StatusCode}.",
                    startedAtUtc,
                    sw.ElapsedMilliseconds,
                    ct);

                return Result<ViettelInvoiceSendEmailResultDto>.Failure(
                    Error.Validation(
                        "Viettel.SendEmailHttpFailed",
                        $"Gửi email Viettel thất bại. HTTP {(int)response.StatusCode}. Response: {Trim(raw, 1000)}"));
            }

            var parsed = ParseResponse(
                invoiceHeadId,
                transactionUuid,
                normalizedEmail,
                providerInvoiceNo,
                raw,
                sw.ElapsedMilliseconds);

            await WriteLogAsync(
                invoiceHeadId,
                url,
                requestBodyJson,
                raw,
                parsed.IsSuccess,
                parsed.IsSuccess ? null : parsed.Code,
                parsed.IsSuccess ? null : parsed.Message,
                startedAtUtc,
                sw.ElapsedMilliseconds,
                ct);

            if (!parsed.IsSuccess)
            {
                return Result<ViettelInvoiceSendEmailResultDto>.Failure(
                    Error.Validation(
                        "Viettel.SendEmailBusinessFailed",
                        $"Viettel không gửi được email. Mã lỗi: {parsed.Code}. Nội dung: {parsed.Message}"));
            }

            return Result<ViettelInvoiceSendEmailResultDto>.Success(parsed);
        }
        catch (TaskCanceledException)
        {
            sw.Stop();

            await WriteLogAsync(
                invoiceHeadId,
                url,
                requestBodyJson,
                null,
                false,
                "TIMEOUT",
                "Gửi email Viettel timeout.",
                startedAtUtc,
                sw.ElapsedMilliseconds,
                CancellationToken.None);

            return Result<ViettelInvoiceSendEmailResultDto>.Failure(
                Error.Validation("Viettel.SendEmailTimeout", "Gửi email Viettel timeout. Thử lại sau."));
        }
        catch (Exception ex)
        {
            sw.Stop();

            await WriteLogAsync(
                invoiceHeadId,
                url,
                requestBodyJson,
                null,
                false,
                "EXCEPTION",
                ex.Message,
                startedAtUtc,
                sw.ElapsedMilliseconds,
                CancellationToken.None);

            return Result<ViettelInvoiceSendEmailResultDto>.Failure(
                Error.Validation("Viettel.SendEmailException", $"Lỗi gửi email Viettel: {ex.Message}"));
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
                Code = "EMPTY_RESPONSE",
                Message = "Viettel không trả dữ liệu.",
                RawResponse = string.Empty,
                DurationMs = durationMs
            };
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            var code =
                FindStringProperty(root, "code")
                ?? FindStringProperty(root, "errorCode");

            var message =
                FindStringProperty(root, "message")
                ?? FindStringProperty(root, "description");

            var isSuccess =
                string.Equals(code, "200", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(message, "OK", StringComparison.OrdinalIgnoreCase);

            return new ViettelInvoiceSendEmailResultDto
            {
                InvoiceHeadId = invoiceHeadId,
                IsSuccess = isSuccess,
                BuyerEmail = buyerEmail,
                TransactionUuid = transactionUuid,
                ProviderInvoiceNo = providerInvoiceNo,
                Code = code,
                Message = message,
                RawResponse = Trim(raw, 10000),
                DurationMs = durationMs
            };
        }
        catch (Exception ex)
        {
            return new ViettelInvoiceSendEmailResultDto
            {
                InvoiceHeadId = invoiceHeadId,
                IsSuccess = false,
                BuyerEmail = buyerEmail,
                TransactionUuid = transactionUuid,
                ProviderInvoiceNo = providerInvoiceNo,
                Code = "PARSE_ERROR",
                Message = $"Không đọc được response gửi email: {ex.Message}",
                RawResponse = Trim(raw, 10000),
                DurationMs = durationMs
            };
        }
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
            // Không để lỗi ghi log làm hỏng gửi email.
        }
    }

    private static AuthenticationHeaderValue BuildBasicAuthHeader(
        string username,
        string password)
    {
        var rawCredential = $"{username.Trim()}:{password}";
        var base64Credential = Convert.ToBase64String(Encoding.UTF8.GetBytes(rawCredential));

        return new AuthenticationHeaderValue("Basic", base64Credential);
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

    private static string Trim(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        value = value.Trim();

        return value.Length <= maxLength
            ? value
            : value[..maxLength] + "...";
    }

    private static string? TrimNullable(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();

        return value.Length <= maxLength
            ? value
            : value[..maxLength] + "...";
    }
}