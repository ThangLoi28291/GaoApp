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
                Error.Validation("Viettel.InvoiceHeadIdInvalid", "InvoiceHeadId không hợp lệ."));
        }

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return Result<ViettelInvoiceIssueResultDto>.Failure(
                Error.Validation("Viettel.BaseUrlRequired", "BaseUrl Viettel không được trống."));
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            return Result<ViettelInvoiceIssueResultDto>.Failure(
                Error.Validation("Viettel.UsernameRequired", "Username Viettel không được trống."));
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            return Result<ViettelInvoiceIssueResultDto>.Failure(
                Error.Validation("Viettel.PasswordRequired", "Password Viettel không được trống."));
        }

        if (string.IsNullOrWhiteSpace(supplierTaxCode))
        {
            return Result<ViettelInvoiceIssueResultDto>.Failure(
                Error.Validation("Viettel.SupplierTaxCodeRequired", "MST phát hành không được trống."));
        }

        if (authMode != InvoiceProviderAuthMode.BasicAuth)
        {
            return Result<ViettelInvoiceIssueResultDto>.Failure(
                Error.Validation(
                    "Viettel.AuthModeUnsupported",
                    "Phát hành hiện dùng Basic Auth theo tài khoản Viettel của bạn. Vui lòng chọn Basic Auth."));
        }

        var url =
            $"{baseUrl.TrimEnd('/')}/InvoiceAPI/InvoiceWS/createInvoice/{Uri.EscapeDataString(supplierTaxCode.Trim())}";

        var startedAtUtc = DateTime.UtcNow;
        var sw = Stopwatch.StartNew();

        var requestBodyJson = JsonSerializer.Serialize(
            payload,
            new JsonSerializerOptions
            {
                WriteIndented = false,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

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
                    RawResponse = Trim(responseText, 10000),
                    DurationMs = sw.ElapsedMilliseconds
                };

                await WriteIssueLogAsync(
                    invoiceHeadId,
                    url,
                    requestBodyJson,
                    responseText,
                    false,
                    failDto.ErrorCode,
                    failDto.ErrorMessage,
                    startedAtUtc,
                    sw.ElapsedMilliseconds,
                    ct);

                return Result<ViettelInvoiceIssueResultDto>.Success(failDto);
            }

            var parsed = ParseIssueResponse(
                invoiceHeadId,
                responseText,
                sw.ElapsedMilliseconds);

            await WriteIssueLogAsync(
                invoiceHeadId,
                url,
                requestBodyJson,
                responseText,
                parsed.IsSuccess,
                parsed.ErrorCode,
                parsed.ErrorMessage,
                startedAtUtc,
                sw.ElapsedMilliseconds,
                ct);

            return Result<ViettelInvoiceIssueResultDto>.Success(parsed);
        }
        catch (TaskCanceledException)
        {
            sw.Stop();

            var message = "Gọi Viettel phát hành timeout. Cần tra cứu lại bằng transactionUuid trước khi bấm phát hành lại.";

            await WriteIssueLogAsync(
                invoiceHeadId,
                url,
                requestBodyJson,
                null,
                false,
                "TIMEOUT",
                message,
                startedAtUtc,
                sw.ElapsedMilliseconds,
                CancellationToken.None);

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

            var errorCode = FindStringProperty(root, "errorCode")
                ?? FindStringProperty(root, "code");

            var description = FindStringProperty(root, "description")
                ?? FindStringProperty(root, "message");

            var isError =
                !string.IsNullOrWhiteSpace(errorCode) &&
                !string.Equals(errorCode, "null", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(errorCode, "200", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(errorCode, "OK", StringComparison.OrdinalIgnoreCase);

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
                    RawResponse = Trim(responseText, 10000),
                    DurationMs = durationMs
                };
            }

            var invoiceNo =
                FindStringProperty(root, "invoiceNo")
                ?? FindStringProperty(root, "invoiceNumber");

            var transactionId =
                FindStringProperty(root, "transactionID")
                ?? FindStringProperty(root, "transactionId")
                ?? FindStringProperty(root, "transactionIDStr")
                ?? FindStringProperty(root, "invoiceId");

            var reservationCode =
                FindStringProperty(root, "reservationCode");

            var codeOfTax =
                FindStringProperty(root, "codeOfTax");

            var success = !string.IsNullOrWhiteSpace(invoiceNo)
                          || !string.IsNullOrWhiteSpace(transactionId)
                          || string.IsNullOrWhiteSpace(errorCode)
                          || string.Equals(errorCode, "null", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(errorCode, "200", StringComparison.OrdinalIgnoreCase);

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
                RawResponse = Trim(responseText, 10000),
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
                RawResponse = Trim(responseText, 10000),
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
            // Không để lỗi ghi log làm hỏng kết quả phát hành.
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