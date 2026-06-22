using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using System.Diagnostics;
using System.Globalization;
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
                Error.Validation(
                    "Viettel.InvoiceHeadIdInvalid",
                    "InvoiceHeadId không hợp lệ."));
        }

        if (authMode != InvoiceProviderAuthMode.BasicAuth)
        {
            return Result<ViettelOfficialFileResultDto>.Failure(
                Error.Validation(
                    "Viettel.AuthModeUnsupported",
                    "Hiện tại tải file Viettel đang dùng Basic Auth."));
        }

        if (string.IsNullOrWhiteSpace(baseUrl) ||
            string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password) ||
            string.IsNullOrWhiteSpace(supplierTaxCode) ||
            string.IsNullOrWhiteSpace(invoiceNo) ||
            string.IsNullOrWhiteSpace(templateCode))
        {
            return Result<ViettelOfficialFileResultDto>.Failure(
                Error.Validation(
                    "Viettel.RequiredMissing",
                    "Thiếu thông tin để tải file hóa đơn Viettel."));
        }

        var actionType = fileType == ViettelOfficialFileType.Pdf
            ? InvoiceIntegrationActionType.DownloadPdf
            : InvoiceIntegrationActionType.DownloadZip;

        var startedAtUtc = DateTime.UtcNow;
        var sw = Stopwatch.StartNew();

        var normalizedBaseUrl = ViettelClientHelper.NormalizeBaseUrl(baseUrl);
        var url = $"{normalizedBaseUrl}/InvoiceAPI/InvoiceUtilsWS/getInvoiceRepresentationFile";

        var representationBody = new
        {
            supplierTaxCode = supplierTaxCode.Trim(),
            invoiceNo = invoiceNo.Trim(),
            templateCode = templateCode.Trim(),
            fileType = fileType == ViettelOfficialFileType.Pdf
                ? "PDF"
                : "ZIP"
        };

        var requestBodyForLog = ViettelClientHelper.SerializeJson(representationBody);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);

            request.Headers.Accept.Clear();
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.Accept.ParseAdd("application/pdf");
            request.Headers.Accept.ParseAdd("application/zip");
            request.Headers.Accept.ParseAdd("application/octet-stream");

            request.Headers.Authorization = ViettelClientHelper.BuildBasicAuthHeader(
                username,
                password);

            request.Content = new StringContent(
                requestBodyForLog,
                Encoding.UTF8,
                "application/json");

            using var response = await _httpClient.SendAsync(request, ct);

            var responseBytes = await response.Content.ReadAsByteArrayAsync(ct);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;

            sw.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var raw = ViettelClientHelper.SafeReadText(responseBytes);

                await WriteLogAsync(
                    invoiceHeadId: invoiceHeadId,
                    actionType: actionType,
                    requestUrl: url,
                    requestBody: requestBodyForLog,
                    responseBody: raw,
                    isSuccess: false,
                    errorCode: $"HTTP_{(int)response.StatusCode}",
                    errorMessage: $"Tải file Viettel thất bại. HTTP {(int)response.StatusCode}.",
                    startedAtUtc: startedAtUtc,
                    durationMs: sw.ElapsedMilliseconds,
                    ct: ct);

                return Result<ViettelOfficialFileResultDto>.Failure(
                    Error.Validation(
                        "Viettel.DownloadFileFailed",
                        $"Tải file Viettel thất bại. HTTP {(int)response.StatusCode}. Response: {ViettelClientHelper.Trim(raw, 1000)}"));
            }

            if (fileType == ViettelOfficialFileType.Pdf &&
                (ViettelClientHelper.IsPdfBytes(responseBytes) ||
                 contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase)))
            {
                await WriteLogAsync(
                    invoiceHeadId: invoiceHeadId,
                    actionType: actionType,
                    requestUrl: url,
                    requestBody: requestBodyForLog,
                    responseBody: ViettelClientHelper.BuildBinarySummary(
                        responseBytes,
                        contentType,
                        "pdf"),
                    isSuccess: true,
                    errorCode: null,
                    errorMessage: null,
                    startedAtUtc: startedAtUtc,
                    durationMs: sw.ElapsedMilliseconds,
                    ct: ct);

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
                (ViettelClientHelper.IsZipBytes(responseBytes) ||
                 contentType.Contains("zip", StringComparison.OrdinalIgnoreCase) ||
                 contentType.Contains("octet-stream", StringComparison.OrdinalIgnoreCase)))
            {
                await WriteLogAsync(
                    invoiceHeadId: invoiceHeadId,
                    actionType: actionType,
                    requestUrl: url,
                    requestBody: requestBodyForLog,
                    responseBody: ViettelClientHelper.BuildBinarySummary(
                        responseBytes,
                        contentType,
                        "zip"),
                    isSuccess: true,
                    errorCode: null,
                    errorMessage: null,
                    startedAtUtc: startedAtUtc,
                    durationMs: sw.ElapsedMilliseconds,
                    ct: ct);

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

            var rawText = ViettelClientHelper.SafeReadText(responseBytes);

            var parsed = TryParseFileResponse(
                invoiceHeadId,
                fileType,
                invoiceNo,
                rawText);

            if (parsed != null)
            {
                await WriteLogAsync(
                    invoiceHeadId: invoiceHeadId,
                    actionType: actionType,
                    requestUrl: url,
                    requestBody: requestBodyForLog,
                    responseBody: rawText,
                    isSuccess: parsed.IsSuccess,
                    errorCode: parsed.IsSuccess ? null : "VIETTEL_FILE_RESPONSE_ERROR",
                    errorMessage: parsed.IsSuccess ? null : parsed.Error?.Message,
                    startedAtUtc: startedAtUtc,
                    durationMs: sw.ElapsedMilliseconds,
                    ct: ct);

                return parsed;
            }

            await WriteLogAsync(
                invoiceHeadId: invoiceHeadId,
                actionType: actionType,
                requestUrl: url,
                requestBody: requestBodyForLog,
                responseBody: rawText,
                isSuccess: false,
                errorCode: "UNKNOWN_RESPONSE",
                errorMessage: "Viettel trả response không nhận diện được file.",
                startedAtUtc: startedAtUtc,
                durationMs: sw.ElapsedMilliseconds,
                ct: ct);

            return Result<ViettelOfficialFileResultDto>.Failure(
                Error.Validation(
                    "Viettel.UnknownFileResponse",
                    $"Viettel trả response không nhận diện được file. Content-Type: {contentType}. Response: {ViettelClientHelper.Trim(rawText, 1000)}"));
        }
        catch (TaskCanceledException)
        {
            sw.Stop();

            await WriteLogAsync(
                invoiceHeadId: invoiceHeadId,
                actionType: actionType,
                requestUrl: url,
                requestBody: requestBodyForLog,
                responseBody: null,
                isSuccess: false,
                errorCode: "TIMEOUT",
                errorMessage: "Tải file Viettel timeout.",
                startedAtUtc: startedAtUtc,
                durationMs: sw.ElapsedMilliseconds,
                ct: CancellationToken.None);

            return Result<ViettelOfficialFileResultDto>.Failure(
                Error.Validation(
                    "Viettel.DownloadTimeout",
                    "Tải file Viettel timeout. Thử lại sau."));
        }
        catch (Exception ex)
        {
            sw.Stop();

            await WriteLogAsync(
                invoiceHeadId: invoiceHeadId,
                actionType: actionType,
                requestUrl: url,
                requestBody: requestBodyForLog,
                responseBody: null,
                isSuccess: false,
                errorCode: "EXCEPTION",
                errorMessage: ex.Message,
                startedAtUtc: startedAtUtc,
                durationMs: sw.ElapsedMilliseconds,
                ct: CancellationToken.None);

            return Result<ViettelOfficialFileResultDto>.Failure(
                Error.Validation(
                    "Viettel.DownloadException",
                    $"Lỗi tải file Viettel: {ex.Message}"));
        }
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

            var errorCode =
                ViettelClientHelper.FindStringProperty(root, "errorCode") ??
                ViettelClientHelper.FindStringProperty(root, "code");

            var description =
                ViettelClientHelper.FindStringProperty(root, "description") ??
                ViettelClientHelper.FindStringProperty(root, "message") ??
                ViettelClientHelper.FindStringProperty(root, "data");

            if (!ViettelClientHelper.IsViettelSuccessCode(errorCode))
            {
                return Result<ViettelOfficialFileResultDto>.Failure(
                    Error.Validation(
                        "Viettel.FileBusinessError",
                        $"Viettel trả lỗi tải file: {errorCode} - {description}"));
            }

            var fileBytes = ViettelClientHelper.TryReadFileToBytes(root);

            if (fileBytes == null || fileBytes.Length == 0)
            {
                return Result<ViettelOfficialFileResultDto>.Failure(
                    Error.Validation(
                        "Viettel.NoFileToBytes",
                        $"Viettel không trả fileToBytes. Response: {ViettelClientHelper.Trim(rawText, 1000)}"));
            }

            var fileName = ViettelClientHelper.FindStringProperty(root, "fileName");

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
                    RawResponsePreview = ViettelClientHelper.Trim(rawText, 1000)
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
            // Không để lỗi ghi log làm hỏng tải file.
        }
    }

    // Tạm giữ lại để sau này nếu cần tra ngày phát hành từ getInvoices.
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
        var normalizedBaseUrl = ViettelClientHelper.NormalizeBaseUrl(baseUrl);

        var url =
            $"{normalizedBaseUrl}/InvoiceAPI/InvoiceUtilsWS/getInvoices/{Uri.EscapeDataString(supplierTaxCode.Trim())}";

        var from = fallbackIssuedAtUtc.Date.AddDays(-2).ToString("yyyy-MM-dd");
        var to = fallbackIssuedAtUtc.Date.AddDays(2).ToString("yyyy-MM-dd");

        using var request = new HttpRequestMessage(HttpMethod.Post, url);

        request.Headers.Accept.Clear();
        request.Headers.Accept.ParseAdd("application/json");

        request.Headers.Authorization = ViettelClientHelper.BuildBasicAuthHeader(
            username,
            password);

        var bodyJson = ViettelClientHelper.SerializeJson(new
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
                    $"Không tra được ngày phát hành từ getInvoices. HTTP {(int)response.StatusCode}. Response: {ViettelClientHelper.Trim(raw, 1000)}"));
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

        if (!ViettelClientHelper.TryFindProperty(doc.RootElement, "invoices", out var invoicesElement))
            return null;

        if (invoicesElement.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var item in invoicesElement.EnumerateArray())
        {
            var currentInvoiceNo =
                ViettelClientHelper.FindStringProperty(item, "invoiceNo");

            if (!string.Equals(currentInvoiceNo, invoiceNo, StringComparison.OrdinalIgnoreCase))
                continue;

            return
                ViettelClientHelper.FindStringProperty(item, "issueDateStr") ??
                ViettelClientHelper.FindStringProperty(item, "issueDate");
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
}