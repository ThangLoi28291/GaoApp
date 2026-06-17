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

public class ViettelOfficialFileClient : IViettelOfficialFileClient
{
    private readonly HttpClient _httpClient;
    private readonly IInvoiceIntegrationLogRepository _logRepository;

    public ViettelOfficialFileClient(
        HttpClient httpClient,
        IInvoiceIntegrationLogRepository logRepository)
    {
        _httpClient = httpClient;
        _logRepository = logRepository;
    }

    public async Task<Result<ViettelOfficialFileResultDto>> DownloadOfficialFileAsync(
     int invoiceHeadId,
     ViettelOfficialFileType fileType,
     string baseUrl,
     string username,
     string password,
     InvoiceProviderAuthMode authMode,
     string supplierTaxCode,
     string invoiceNo,
     string templateCode,
     string invoiceSeries,
     DateTime issuedAtUtc,
     CancellationToken ct = default)
    {
        if (invoiceHeadId <= 0)
        {
            return Result<ViettelOfficialFileResultDto>.Failure(
                Error.Validation("Viettel.InvoiceHeadIdInvalid", "InvoiceHeadId không hợp lệ."));
        }

        if (authMode != InvoiceProviderAuthMode.BasicAuth)
        {
            return Result<ViettelOfficialFileResultDto>.Failure(
                Error.Validation("Viettel.AuthModeUnsupported", "Hiện tại tải file Viettel đang dùng Basic Auth."));
        }

        if (string.IsNullOrWhiteSpace(baseUrl) ||
            string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password) ||
            string.IsNullOrWhiteSpace(supplierTaxCode) ||
            string.IsNullOrWhiteSpace(invoiceNo) ||
            string.IsNullOrWhiteSpace(templateCode))
        {
            return Result<ViettelOfficialFileResultDto>.Failure(
                Error.Validation("Viettel.RequiredMissing", "Thiếu thông tin để tải file hóa đơn Viettel."));
        }

        var actionType = fileType == ViettelOfficialFileType.Pdf
            ? InvoiceIntegrationActionType.DownloadPdf
            : InvoiceIntegrationActionType.DownloadZip;

        var startedAtUtc = DateTime.UtcNow;
        var sw = Stopwatch.StartNew();

        string url;
        string requestBodyForLog;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "");

            request.Headers.Accept.Clear();
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.Accept.ParseAdd("application/pdf");
            request.Headers.Accept.ParseAdd("application/zip");
            request.Headers.Accept.ParseAdd("application/octet-stream");

            request.Headers.Authorization = BuildBasicAuthHeader(username, password);

            if (fileType == ViettelOfficialFileType.Pdf)
            {
                // PDF chính thức chuyển đổi:
                // API này dùng x-www-form-urlencoded.
                var issueMillisResult = await ResolveIssueDateMillisecondsAsync(
                    baseUrl: baseUrl,
                    username: username,
                    password: password,
                    supplierTaxCode: supplierTaxCode,
                    invoiceNo: invoiceNo,
                    templateCode: templateCode,
                    invoiceSeries: invoiceSeries,
                    fallbackIssuedAtUtc: issuedAtUtc,
                    ct: ct);

                if (!issueMillisResult.IsSuccess)
                {
                    return Result<ViettelOfficialFileResultDto>.Failure(issueMillisResult.Error!);
                }

                var issueMillis = issueMillisResult.Value;

                url = $"{baseUrl.TrimEnd('/')}/InvoiceAPI/InvoiceWS/createExchangeInvoiceFile";

                var form = new Dictionary<string, string>
                {
                    ["supplierTaxCode"] = supplierTaxCode.Trim(),
                    ["invoiceNo"] = invoiceNo.Trim(),
                    ["strIssueDate"] = issueMillis.ToString(),
                    ["exchangeUser"] = "GaoApp"
                };

                request.RequestUri = new Uri(url);
                request.Content = new FormUrlEncodedContent(form);

                requestBodyForLog = string.Join(
                    "&",
                    form.Select(x => $"{x.Key}={x.Value}"));
            }
            else
            {
                // ZIP/XML representation:
                // API này bắt buộc dùng application/json.
                url = $"{baseUrl.TrimEnd('/')}/InvoiceAPI/InvoiceUtilsWS/getInvoiceRepresentationFile";

                var body = new
                {
                    supplierTaxCode = supplierTaxCode.Trim(),
                    invoiceNo = invoiceNo.Trim(),
                    templateCode = templateCode.Trim(),
                    fileType = "ZIP"
                };

                requestBodyForLog = JsonSerializer.Serialize(body);

                request.RequestUri = new Uri(url);
                request.Content = new StringContent(
                    requestBodyForLog,
                    Encoding.UTF8,
                    "application/json");
            }

            using var response = await _httpClient.SendAsync(request, ct);

            var responseBytes = await response.Content.ReadAsByteArrayAsync(ct);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;

            sw.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var raw = SafeReadText(responseBytes);

                await WriteLogAsync(
                    invoiceHeadId,
                    actionType,
                    url,
                    requestBodyForLog,
                    raw,
                    false,
                    $"HTTP_{(int)response.StatusCode}",
                    $"Tải file Viettel thất bại. HTTP {(int)response.StatusCode}.",
                    startedAtUtc,
                    sw.ElapsedMilliseconds,
                    ct);

                return Result<ViettelOfficialFileResultDto>.Failure(
                    Error.Validation(
                        "Viettel.DownloadFileFailed",
                        $"Tải file Viettel thất bại. HTTP {(int)response.StatusCode}. Response: {Trim(raw, 1000)}"));
            }

            if (fileType == ViettelOfficialFileType.Pdf &&
                (IsPdfBytes(responseBytes) ||
                 contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase)))
            {
                await WriteLogAsync(
                    invoiceHeadId,
                    actionType,
                    url,
                    requestBodyForLog,
                    BuildBinarySummary(responseBytes, contentType, "pdf"),
                    true,
                    null,
                    null,
                    startedAtUtc,
                    sw.ElapsedMilliseconds,
                    ct);

                return Result<ViettelOfficialFileResultDto>.Success(
                    new ViettelOfficialFileResultDto
                    {
                        InvoiceHeadId = invoiceHeadId,
                        FileType = fileType,
                        FileName = $"{invoiceNo}.pdf",
                        ContentType = "application/pdf",
                        FileBytes = responseBytes
                    });
            }

            if (fileType == ViettelOfficialFileType.ZipXml &&
                (IsZipBytes(responseBytes) ||
                 contentType.Contains("zip", StringComparison.OrdinalIgnoreCase) ||
                 contentType.Contains("octet-stream", StringComparison.OrdinalIgnoreCase)))
            {
                await WriteLogAsync(
                    invoiceHeadId,
                    actionType,
                    url,
                    requestBodyForLog,
                    BuildBinarySummary(responseBytes, contentType, "zip"),
                    true,
                    null,
                    null,
                    startedAtUtc,
                    sw.ElapsedMilliseconds,
                    ct);

                return Result<ViettelOfficialFileResultDto>.Success(
                    new ViettelOfficialFileResultDto
                    {
                        InvoiceHeadId = invoiceHeadId,
                        FileType = fileType,
                        FileName = $"{invoiceNo}.zip",
                        ContentType = "application/zip",
                        FileBytes = responseBytes
                    });
            }

            var rawText = SafeReadText(responseBytes);

            var parsed = TryParseFileResponse(
                invoiceHeadId,
                fileType,
                invoiceNo,
                rawText);

            if (parsed != null)
            {
                await WriteLogAsync(
                    invoiceHeadId,
                    actionType,
                    url,
                    requestBodyForLog,
                    rawText,
                    parsed.IsSuccess,
                    parsed.IsSuccess ? null : "VIETTEL_FILE_RESPONSE_ERROR",
                    parsed.IsSuccess ? null : parsed.Error?.Message,
                    startedAtUtc,
                    sw.ElapsedMilliseconds,
                    ct);

                return parsed;
            }

            await WriteLogAsync(
                invoiceHeadId,
                actionType,
                url,
                requestBodyForLog,
                rawText,
                false,
                "UNKNOWN_RESPONSE",
                "Viettel trả response không nhận diện được file.",
                startedAtUtc,
                sw.ElapsedMilliseconds,
                ct);

            return Result<ViettelOfficialFileResultDto>.Failure(
                Error.Validation(
                    "Viettel.UnknownFileResponse",
                    $"Viettel trả response không nhận diện được file. Content-Type: {contentType}. Response: {Trim(rawText, 1000)}"));
        }
        catch (TaskCanceledException)
        {
            sw.Stop();

            await WriteLogAsync(
                invoiceHeadId,
                actionType,
                null,
                null,
                null,
                false,
                "TIMEOUT",
                "Tải file Viettel timeout.",
                startedAtUtc,
                sw.ElapsedMilliseconds,
                CancellationToken.None);

            return Result<ViettelOfficialFileResultDto>.Failure(
                Error.Validation("Viettel.DownloadTimeout", "Tải file Viettel timeout. Thử lại sau."));
        }
        catch (Exception ex)
        {
            sw.Stop();

            await WriteLogAsync(
                invoiceHeadId,
                actionType,
                null,
                null,
                null,
                false,
                "EXCEPTION",
                ex.Message,
                startedAtUtc,
                sw.ElapsedMilliseconds,
                CancellationToken.None);

            return Result<ViettelOfficialFileResultDto>.Failure(
                Error.Validation("Viettel.DownloadException", $"Lỗi tải file Viettel: {ex.Message}"));
        }
    }
    private async Task<Result<long>> ResolveIssueDateMillisecondsAsync(
    string baseUrl,
    string username,
    string password,
    string supplierTaxCode,
    string invoiceNo,
    string templateCode,
    string invoiceSeries,
    DateTime fallbackIssuedAtUtc,
    CancellationToken ct)
    {
        var url =
            $"{baseUrl.TrimEnd('/')}/InvoiceAPI/InvoiceUtilsWS/getInvoices/{Uri.EscapeDataString(supplierTaxCode.Trim())}";

        var from = fallbackIssuedAtUtc.Date.AddDays(-2).ToString("yyyy-MM-dd");
        var to = fallbackIssuedAtUtc.Date.AddDays(2).ToString("yyyy-MM-dd");

        using var request = new HttpRequestMessage(HttpMethod.Post, url);

        request.Headers.Accept.Clear();
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Authorization = BuildBasicAuthHeader(username, password);

        var bodyJson = JsonSerializer.Serialize(new
        {
            startDate = from,
            endDate = to,
            invoiceType = "1",
            rowPerPage = 100,
            pageNum = 1,
            templateCode = templateCode,
            invoiceSeri = invoiceSeries,
            getAll = true
        });

        request.Content = new StringContent(
            bodyJson,
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            return Result<long>.Failure(
                Error.Validation(
                    "Viettel.ResolveIssueDateFailed",
                    $"Không tra được ngày phát hành từ getInvoices. HTTP {(int)response.StatusCode}. Response: {Trim(raw, 1000)}"));
        }

        var issueDateStr = FindInvoiceIssueDateStr(raw, invoiceNo);

        if (string.IsNullOrWhiteSpace(issueDateStr))
        {
            return Result<long>.Failure(
                Error.Validation(
                    "Viettel.InvoiceNotFoundOnGetInvoices",
                    $"Không tìm thấy hóa đơn {invoiceNo} trên getInvoices trong khoảng {from} đến {to}. Không thể tải PDF chính thức."));
        }

        var issueMillis = TryParseIssueDateStrToMillis(issueDateStr);

        if (!issueMillis.HasValue)
        {
            return Result<long>.Failure(
                Error.Validation(
                    "Viettel.IssueDateInvalid",
                    $"Không convert được issueDateStr của Viettel: {issueDateStr}."));
        }

        return Result<long>.Success(issueMillis.Value);
    }

    private static string? FindInvoiceIssueDateStr(
        string rawJson,
        string invoiceNo)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return null;

        using var doc = JsonDocument.Parse(rawJson);

        if (!TryFindProperty(doc.RootElement, "invoices", out var invoicesElement))
            return null;

        if (invoicesElement.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var item in invoicesElement.EnumerateArray())
        {
            var currentInvoiceNo = FindStringProperty(item, "invoiceNo");

            if (!string.Equals(currentInvoiceNo, invoiceNo, StringComparison.OrdinalIgnoreCase))
                continue;

            return FindStringProperty(item, "issueDateStr")
                ?? FindStringProperty(item, "issueDate");
        }

        return null;
    }

    private static long? TryParseIssueDateStrToMillis(string issueDateStr)
    {
        if (string.IsNullOrWhiteSpace(issueDateStr))
            return null;

        issueDateStr = issueDateStr.Trim();

        if (long.TryParse(issueDateStr, out var millis))
            return millis;

        if (DateTimeOffset.TryParse(
                issueDateStr,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dto))
        {
            return dto.ToUnixTimeMilliseconds();
        }

        return null;
    }
    private static Result<ViettelOfficialFileResultDto>? TryParseFileResponse(
        int invoiceHeadId,
        ViettelOfficialFileType fileType,
        string invoiceNo,
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

            var errorCode = FindStringProperty(root, "errorCode")
                ?? FindStringProperty(root, "code");

            var description = FindStringProperty(root, "description")
                ?? FindStringProperty(root, "message")
                ?? FindStringProperty(root, "data");

            if (!string.IsNullOrWhiteSpace(errorCode) &&
                !string.Equals(errorCode, "null", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(errorCode, "200", StringComparison.OrdinalIgnoreCase))
            {
                return Result<ViettelOfficialFileResultDto>.Failure(
                    Error.Validation(
                        "Viettel.FileBusinessError",
                        $"Viettel trả lỗi tải file: {errorCode} - {description}"));
            }

            var fileBytes = TryReadFileToBytes(root);

            if (fileBytes == null || fileBytes.Length == 0)
            {
                return Result<ViettelOfficialFileResultDto>.Failure(
                    Error.Validation(
                        "Viettel.NoFileToBytes",
                        $"Viettel không trả fileToBytes. Response: {Trim(rawText, 1000)}"));
            }

            var fileName = FindStringProperty(root, "fileName");

            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = fileType == ViettelOfficialFileType.Pdf
                    ? $"{invoiceNo}.pdf"
                    : $"{invoiceNo}.zip";
            }

            var contentType = fileType == ViettelOfficialFileType.Pdf
                ? "application/pdf"
                : "application/zip";

            return Result<ViettelOfficialFileResultDto>.Success(
                new ViettelOfficialFileResultDto
                {
                    InvoiceHeadId = invoiceHeadId,
                    FileType = fileType,
                    FileName = fileName,
                    ContentType = contentType,
                    FileBytes = fileBytes,
                    RawResponsePreview = Trim(rawText, 1000)
                });
        }
        catch
        {
            return null;
        }
    }

    private async Task WriteLogAsync(
        int invoiceHeadId,
        InvoiceIntegrationActionType actionType,
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
                ActionType = actionType,
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
            // Không để lỗi ghi log làm hỏng tải file.
        }
    }

    private static long ToUnixMilliseconds(DateTime dateTime)
    {
        if (dateTime.Kind == DateTimeKind.Unspecified)
            dateTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);

        if (dateTime.Kind == DateTimeKind.Local)
            dateTime = dateTime.ToUniversalTime();

        return new DateTimeOffset(dateTime).ToUnixTimeMilliseconds();
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

    private static bool IsZipBytes(byte[] bytes)
    {
        return bytes.Length >= 4 &&
               bytes[0] == 0x50 &&
               bytes[1] == 0x4B;
    }

    private static string BuildBinarySummary(byte[] bytes, string contentType, string fileKind)
    {
        return JsonSerializer.Serialize(new
        {
            contentType,
            fileKind,
            fileSize = bytes.Length,
            note = "Không lưu file bytes vào log để tránh DB phình lớn."
        });
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

    private static string? FindStringProperty(JsonElement element, string propertyName)
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