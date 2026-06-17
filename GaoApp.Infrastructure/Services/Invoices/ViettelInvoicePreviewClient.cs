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

public class ViettelInvoicePreviewClient : IViettelInvoicePreviewClient
{
    private readonly HttpClient _httpClient;
    private readonly IInvoiceIntegrationLogRepository _logRepository;

    public ViettelInvoicePreviewClient(
        HttpClient httpClient,
        IInvoiceIntegrationLogRepository logRepository)
    {
        _httpClient = httpClient;
        _logRepository = logRepository;
    }

    public async Task<Result<ViettelInvoicePreviewFileDto>> CreateDraftPreviewAsync(
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
            return Result<ViettelInvoicePreviewFileDto>.Failure(
                Error.Validation("Viettel.InvoiceHeadIdInvalid", "InvoiceHeadId không hợp lệ."));
        }

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return Result<ViettelInvoicePreviewFileDto>.Failure(
                Error.Validation("Viettel.BaseUrlRequired", "BaseUrl Viettel không được trống."));
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            return Result<ViettelInvoicePreviewFileDto>.Failure(
                Error.Validation("Viettel.UsernameRequired", "Username Viettel không được trống."));
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            return Result<ViettelInvoicePreviewFileDto>.Failure(
                Error.Validation("Viettel.PasswordRequired", "Password Viettel không được trống."));
        }

        if (string.IsNullOrWhiteSpace(supplierTaxCode))
        {
            return Result<ViettelInvoicePreviewFileDto>.Failure(
                Error.Validation("Viettel.SupplierTaxCodeRequired", "MST phát hành không được trống."));
        }

        if (authMode != InvoiceProviderAuthMode.BasicAuth)
        {
            return Result<ViettelInvoicePreviewFileDto>.Failure(
                Error.Validation(
                    "Viettel.AuthModeUnsupported",
                    "Preview hiện đang dùng Basic Auth theo tài khoản Viettel của bạn. Vui lòng chọn Basic Auth."));
        }

        var url =
            $"{baseUrl.TrimEnd('/')}/InvoiceAPI/InvoiceUtilsWS/createInvoiceDraftPreview/{Uri.EscapeDataString(supplierTaxCode.Trim())}";

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
            request.Headers.Accept.ParseAdd("application/pdf");
            request.Headers.Accept.ParseAdd("application/json");

            request.Headers.Authorization = BuildBasicAuthHeader(username, password);

            // Gửi đúng JSON đã serialize để log và request khớp nhau.
            request.Content = new StringContent(
                requestBodyJson,
                Encoding.UTF8,
                "application/json");

            using var response = await _httpClient.SendAsync(request, ct);

            var responseBytes = await response.Content.ReadAsByteArrayAsync(ct);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;

            sw.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var raw = SafeReadText(responseBytes);

                await WritePreviewLogAsync(
                    invoiceHeadId: invoiceHeadId,
                    requestUrl: url,
                    requestBody: requestBodyJson,
                    responseBody: raw,
                    isSuccess: false,
                    errorCode: $"HTTP_{(int)response.StatusCode}",
                    errorMessage: $"Viettel preview thất bại. HTTP {(int)response.StatusCode}.",
                    startedAtUtc: startedAtUtc,
                    durationMs: sw.ElapsedMilliseconds,
                    ct: ct);

                return Result<ViettelInvoicePreviewFileDto>.Failure(
                    Error.Validation(
                        "Viettel.PreviewFailed",
                        $"Viettel preview thất bại. HTTP {(int)response.StatusCode}. Response: {Trim(raw, 1000)}"));
            }

            // Trường hợp Viettel trả thẳng file PDF.
            if (IsPdfBytes(responseBytes) ||
                contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase) ||
                contentType.Contains("octet-stream", StringComparison.OrdinalIgnoreCase))
            {
                await WritePreviewLogAsync(
                    invoiceHeadId: invoiceHeadId,
                    requestUrl: url,
                    requestBody: requestBodyJson,
                    responseBody: BuildPdfResponseSummary(responseBytes, contentType),
                    isSuccess: true,
                    errorCode: null,
                    errorMessage: null,
                    startedAtUtc: startedAtUtc,
                    durationMs: sw.ElapsedMilliseconds,
                    ct: ct);

                return Result<ViettelInvoicePreviewFileDto>.Success(
                    new ViettelInvoicePreviewFileDto
                    {
                        InvoiceHeadId = invoiceHeadId,
                        FileName = $"viettel-preview-invoice-{invoiceHeadId}.pdf",
                        ContentType = "application/pdf",
                        FileBytes = responseBytes
                    });
            }

            var rawText = SafeReadText(responseBytes);

            // Trường hợp Viettel trả JSON có errorCode hoặc fileToBytes.
            var parsed = TryParseJsonPreviewResponse(
                invoiceHeadId,
                rawText);

            if (parsed != null)
            {
                await WritePreviewLogAsync(
                    invoiceHeadId: invoiceHeadId,
                    requestUrl: url,
                    requestBody: requestBodyJson,
                    responseBody: rawText,
                    isSuccess: parsed.IsSuccess,
                    errorCode: parsed.IsSuccess ? null : "VIETTEL_PREVIEW_JSON_ERROR",
                    errorMessage: parsed.IsSuccess ? null : parsed.Error?.Message,
                    startedAtUtc: startedAtUtc,
                    durationMs: sw.ElapsedMilliseconds,
                    ct: ct);

                return parsed;
            }

            await WritePreviewLogAsync(
                invoiceHeadId: invoiceHeadId,
                requestUrl: url,
                requestBody: requestBodyJson,
                responseBody: rawText,
                isSuccess: false,
                errorCode: "UNKNOWN_RESPONSE",
                errorMessage: $"Viettel trả response không nhận diện được PDF. Content-Type: {contentType}.",
                startedAtUtc: startedAtUtc,
                durationMs: sw.ElapsedMilliseconds,
                ct: ct);

            return Result<ViettelInvoicePreviewFileDto>.Failure(
                Error.Validation(
                    "Viettel.PreviewUnknownResponse",
                    $"Viettel trả response không nhận diện được PDF. Content-Type: {contentType}. Response: {Trim(rawText, 1000)}"));
        }
        catch (TaskCanceledException)
        {
            sw.Stop();

            await WritePreviewLogAsync(
                invoiceHeadId: invoiceHeadId,
                requestUrl: url,
                requestBody: requestBodyJson,
                responseBody: null,
                isSuccess: false,
                errorCode: "TIMEOUT",
                errorMessage: "Gọi Viettel preview timeout.",
                startedAtUtc: startedAtUtc,
                durationMs: sw.ElapsedMilliseconds,
                ct: CancellationToken.None);

            return Result<ViettelInvoicePreviewFileDto>.Failure(
                Error.Validation(
                    "Viettel.PreviewTimeout",
                    "Gọi Viettel preview timeout. Kiểm tra mạng hoặc thử lại."));
        }
        catch (Exception ex)
        {
            sw.Stop();

            await WritePreviewLogAsync(
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

            return Result<ViettelInvoicePreviewFileDto>.Failure(
                Error.Validation(
                    "Viettel.PreviewException",
                    $"Lỗi khi gọi Viettel preview: {ex.Message}"));
        }
    }

    private async Task WritePreviewLogAsync(
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
                ActionType = InvoiceIntegrationActionType.PreviewDraft,
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
            // Không để lỗi ghi log làm hỏng preview PDF.
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

    private static bool IsPdfBytes(byte[] bytes)
    {
        return bytes.Length >= 4 &&
               bytes[0] == 0x25 &&
               bytes[1] == 0x50 &&
               bytes[2] == 0x44 &&
               bytes[3] == 0x46;
    }

    private static string BuildPdfResponseSummary(
        byte[] bytes,
        string contentType)
    {
        return JsonSerializer.Serialize(new
        {
            contentType,
            fileSize = bytes.Length,
            isPdf = IsPdfBytes(bytes),
            note = "PDF bytes không lưu trực tiếp vào log để tránh DB phình lớn."
        });
    }

    private static Result<ViettelInvoicePreviewFileDto>? TryParseJsonPreviewResponse(
        int invoiceHeadId,
        string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return null;

        var trimmed = rawText.TrimStart();

        if (!trimmed.StartsWith("{") && !trimmed.StartsWith("["))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(rawText);
            var root = doc.RootElement;

            var errorCode = FindStringProperty(root, "errorCode");
            var description = FindStringProperty(root, "description")
                ?? FindStringProperty(root, "message");

            if (!string.IsNullOrWhiteSpace(errorCode) &&
                !string.Equals(errorCode, "null", StringComparison.OrdinalIgnoreCase))
            {
                return Result<ViettelInvoicePreviewFileDto>.Failure(
                    Error.Validation(
                        "Viettel.PreviewBusinessError",
                        $"Viettel trả lỗi preview: {errorCode} - {description}"));
            }

            var fileName = FindStringProperty(root, "fileName");

            var fileBytes = TryReadFileToBytes(root);

            if (fileBytes == null || fileBytes.Length == 0)
            {
                return Result<ViettelInvoicePreviewFileDto>.Failure(
                    Error.Validation(
                        "Viettel.PreviewNoFile",
                        $"Viettel preview thành công nhưng không có fileToBytes. Response: {Trim(rawText, 1000)}"));
            }

            return Result<ViettelInvoicePreviewFileDto>.Success(
                new ViettelInvoicePreviewFileDto
                {
                    InvoiceHeadId = invoiceHeadId,
                    FileName = string.IsNullOrWhiteSpace(fileName)
                        ? $"viettel-preview-invoice-{invoiceHeadId}.pdf"
                        : fileName,
                    ContentType = "application/pdf",
                    FileBytes = fileBytes,
                    RawResponsePreview = Trim(rawText, 1000)
                });
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? TryReadFileToBytes(JsonElement root)
    {
        if (!TryFindProperty(root, "fileToBytes", out var fileElement))
            return null;

        if (fileElement.ValueKind == JsonValueKind.String)
        {
            var value = fileElement.GetString();

            if (string.IsNullOrWhiteSpace(value))
                return null;

            try
            {
                return Convert.FromBase64String(value);
            }
            catch
            {
                return null;
            }
        }

        if (fileElement.ValueKind == JsonValueKind.Array)
        {
            var bytes = new List<byte>();

            foreach (var item in fileElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number &&
                    item.TryGetInt32(out var number) &&
                    number >= 0 &&
                    number <= 255)
                {
                    bytes.Add((byte)number);
                }
            }

            return bytes.ToArray();
        }

        return null;
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

    private static string SafeReadText(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
            return string.Empty;

        try
        {
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return string.Empty;
        }
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