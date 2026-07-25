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

        var normalizedBaseUrl = ViettelClientHelper.NormalizeBaseUrl(baseUrl);

        var url =
            $"{normalizedBaseUrl}/InvoiceAPI/InvoiceUtilsWS/createInvoiceDraftPreview/{Uri.EscapeDataString(supplierTaxCode.Trim())}";

        var startedAtUtc = DateTime.UtcNow;
        var sw = Stopwatch.StartNew();

        var requestBodyJson = ViettelClientHelper.SerializeJson(payload);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);

            request.Headers.Accept.Clear();
            request.Headers.Accept.ParseAdd("application/pdf");
            request.Headers.Accept.ParseAdd("application/json");

            request.Headers.Authorization = ViettelClientHelper.BuildBasicAuthHeader(
                username,
                password);

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
                var raw = ViettelClientHelper.SafeReadText(responseBytes);

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
                        $"Viettel preview thất bại. HTTP {(int)response.StatusCode}."));
            }

            if (ViettelClientHelper.IsPdfBytes(responseBytes))
            {
                await WritePreviewLogAsync(
                    invoiceHeadId: invoiceHeadId,
                    requestUrl: url,
                    requestBody: requestBodyJson,
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

                return Result<ViettelInvoicePreviewFileDto>.Success(
                    new ViettelInvoicePreviewFileDto
                    {
                        InvoiceHeadId = invoiceHeadId,
                        FileName = $"viettel-preview-invoice-{invoiceHeadId}.pdf",
                        ContentType = "application/pdf",
                        FileBytes = responseBytes
                    });
            }

            var rawText = ViettelClientHelper.SafeReadText(responseBytes);

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
                    $"Viettel trả phản hồi không nhận diện được PDF. Content-Type: {contentType}."));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
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
                ct: ct);

            return Result<ViettelInvoicePreviewFileDto>.Failure(
                Error.Validation(
                    "Viettel.PreviewTimeout",
                    "Gọi Viettel preview timeout. Kiểm tra mạng hoặc thử lại."));
        }
        catch (Exception ex)
        {
            sw.Stop();

            var exceptionType = ViettelClientHelper.ExceptionType(ex);

            await WritePreviewLogAsync(
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

            return Result<ViettelInvoicePreviewFileDto>.Failure(
                Error.Validation(
                    "Viettel.PreviewException",
                    "Không gọi được dịch vụ preview Viettel."));
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
                RequestUrl = "Viettel:PreviewInvoice",
                RequestBody = ViettelClientHelper.RedactedRequestSummary("PreviewInvoice"),
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
        catch
        {
            // Không để lỗi ghi log làm hỏng preview PDF.
        }
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
            var jsonValidation =
                ViettelClientHelper.TryParseStrictProviderJson(
                    rawText,
                    out var document);

            if (jsonValidation != ProviderJsonValidationFailure.None ||
                document is null)
            {
                return Result<ViettelInvoicePreviewFileDto>.Failure(
                    Error.Validation(
                        "Viettel.PreviewInvalidResponse",
                        "Không đọc được phản hồi preview Viettel."));
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
                return Result<ViettelInvoicePreviewFileDto>.Failure(
                    Error.Validation(
                        "Viettel.PreviewInvalidResponse",
                        "Viettel trả về envelope preview không hợp lệ."));
            }

            if (containers.Any(
                    container =>
                        ViettelClientHelper.HasDirectFailureMarker(
                            container.Element,
                            ViettelClientHelper.IsExplicitViettelSuccessCode)))
            {
                return Result<ViettelInvoicePreviewFileDto>.Failure(
                    Error.Validation(
                        "Viettel.PreviewBusinessError",
                        "Viettel trả lỗi preview."));
            }

            var candidates = containers
                .Where(
                    container =>
                        ViettelClientHelper.GetUniqueDirectProperty(
                            container.Element,
                            "fileToBytes",
                            out _) ==
                        DirectPropertyLookupResult.Found)
                .ToList();

            if (candidates.Count > 1)
            {
                return Result<ViettelInvoicePreviewFileDto>.Failure(
                    Error.Validation(
                        "Viettel.PreviewConflictingResponse",
                        "Viettel trả về phản hồi mâu thuẫn hoặc không đủ điều kiện xác nhận."));
            }

            if (candidates.Count == 0)
            {
                return Result<ViettelInvoicePreviewFileDto>.Failure(
                    Error.Validation(
                        "Viettel.PreviewNoFile",
                        "Viettel không trả fileToBytes PDF hợp lệ."));
            }

            var payload = candidates[0].Element;
            var fileName =
                ViettelClientHelper.GetDirectString(
                    payload,
                    "fileName");
            var fileBytes =
                ViettelClientHelper.TryGetDirectFileBytes(payload);

            if (fileBytes == null ||
                !ViettelClientHelper.IsPdfBytes(fileBytes))
            {
                return Result<ViettelInvoicePreviewFileDto>.Failure(
                    Error.Validation(
                        "Viettel.PreviewNoFile",
                        "Viettel không trả fileToBytes PDF hợp lệ."));
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
                    RawResponsePreview = null
                });
        }
        catch
        {
            return null;
        }
    }
}
