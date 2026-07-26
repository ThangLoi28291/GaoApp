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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace GaoApp.Infrastructure.Services.Invoices;

public class ViettelInvoiceLookupClient : IViettelInvoiceLookupClient
{
    private readonly HttpClient _httpClient;
    private readonly IInvoiceIntegrationLogRepository _logRepository;
    private readonly ILogger<ViettelInvoiceLookupClient> _logger;

    public ViettelInvoiceLookupClient(
        HttpClient httpClient,
        IInvoiceIntegrationLogRepository logRepository,
        ILogger<ViettelInvoiceLookupClient>? logger = null)
    {
        _httpClient = httpClient;
        _logRepository = logRepository;
        _logger = logger ?? NullLogger<ViettelInvoiceLookupClient>.Instance;
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
                    RawResponse = string.Empty,
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

                return Result<ViettelInvoiceLookupResultDto>.Failure(
                    Error.Validation(fail.ErrorCode, fail.ErrorMessage));
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
                isSuccess: parsed.ErrorCode is null or "NOT_FOUND_DATA",
                errorCode: parsed.ErrorCode,
                errorMessage: parsed.ErrorMessage,
                startedAtUtc: startedAtUtc,
                durationMs: sw.ElapsedMilliseconds,
                ct: ct);

            return parsed.ErrorCode is null or "NOT_FOUND_DATA"
                ? Result<ViettelInvoiceLookupResultDto>.Success(parsed)
                : Result<ViettelInvoiceLookupResultDto>.Failure(
                    Error.Validation(
                        parsed.ErrorCode,
                        parsed.ErrorMessage ?? "Tra cứu Viettel thất bại."));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
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
                ct: ct);

            return Result<ViettelInvoiceLookupResultDto>.Failure(
                Error.Validation("Viettel.LookupTimeout", message));
        }
        catch (Exception ex)
        {
            sw.Stop();

            var exceptionType = ViettelClientHelper.ExceptionType(ex);

            await WriteLookupLogAsync(
                invoiceHeadId: invoiceHeadId,
                requestUrl: url,
                requestBody: requestBodyForLog,
                responseBody: null,
                isSuccess: false,
                errorCode: "EXCEPTION",
                errorMessage: exceptionType,
                startedAtUtc: startedAtUtc,
                durationMs: sw.ElapsedMilliseconds,
                ct: ct);

            return Result<ViettelInvoiceLookupResultDto>.Failure(
                Error.Validation(
                    "Viettel.LookupTransportFailed",
                    "Không gọi được dịch vụ tra cứu Viettel."));
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
                ErrorCode = "Viettel.LookupInvalidResponse",
                ErrorMessage = "Viettel không trả dữ liệu.",
                RawResponse = string.Empty,
                DurationMs = durationMs
            };
        }

        try
        {
            var jsonValidation =
                ViettelClientHelper.TryParseStrictProviderJson(
                    raw,
                    out var document);

            if (jsonValidation != ProviderJsonValidationFailure.None ||
                document is null)
            {
                return new ViettelInvoiceLookupResultDto
                {
                    InvoiceHeadId = invoiceHeadId,
                    IsFound = false,
                    TransactionUuid = transactionUuid,
                    ErrorCode = "Viettel.LookupInvalidResponse",
                    ErrorMessage = "Không đọc được phản hồi tra cứu UUID từ Viettel.",
                    RawResponse = string.Empty,
                    DurationMs = durationMs
                };
            }

            using var strictDocument = document;
            var root = strictDocument.RootElement;
            var containers =
                ViettelClientHelper.GetDirectRecognizedContainers(
                    root,
                    allowResultArray: true,
                    allowDataArray: true,
                    out var hasInvalidEnvelope);
            var locations =
                GetLookupLocations(
                    containers,
                    out var hasInvalidArrayPayload);

            if (hasInvalidEnvelope || hasInvalidArrayPayload)
            {
                return new ViettelInvoiceLookupResultDto
                {
                    InvoiceHeadId = invoiceHeadId,
                    IsFound = false,
                    TransactionUuid = transactionUuid,
                    ErrorCode = "Viettel.LookupInvalidResponse",
                    ErrorMessage = "Viettel trả về envelope tra cứu không hợp lệ.",
                    RawResponse = string.Empty,
                    DurationMs = durationMs
                };
            }

            if (locations.Any(
                    container =>
                        ViettelClientHelper.HasDirectFailureMarker(
                            container.Element,
                            IsAcceptedLookupControlCode)))
            {
                return new ViettelInvoiceLookupResultDto
                {
                    InvoiceHeadId = invoiceHeadId,
                    IsFound = false,
                    TransactionUuid = transactionUuid,
                    ErrorCode = "Viettel.LookupBusinessFailed",
                    ErrorMessage = "Viettel trả lỗi tra cứu hóa đơn.",
                    RawResponse = string.Empty,
                    DurationMs = durationMs
                };
            }

            var candidates = locations
                .Select(
                    location =>
                        new LookupCandidate(
                            GetLookupCandidateKind(location.Element),
                            location.Element))
                .Where(
                    candidate =>
                        candidate.Kind != LookupCandidateKind.None)
                .ToList();

            if (candidates.Any(
                    candidate =>
                        candidate.Kind == LookupCandidateKind.Conflicting) ||
                candidates.Count > 1)
            {
                return new ViettelInvoiceLookupResultDto
                {
                    InvoiceHeadId = invoiceHeadId,
                    IsFound = false,
                    TransactionUuid = transactionUuid,
                    ErrorCode = "Viettel.LookupConflictingResponse",
                    ErrorMessage =
                        "Viettel trả về phản hồi mâu thuẫn hoặc không đủ điều kiện xác nhận.",
                    RawResponse = string.Empty,
                    DurationMs = durationMs
                };
            }

            if (candidates.Count == 0)
            {
                return new ViettelInvoiceLookupResultDto
                {
                    InvoiceHeadId = invoiceHeadId,
                    IsFound = false,
                    TransactionUuid = transactionUuid,
                    ErrorCode = "Viettel.LookupAmbiguousResponse",
                    ErrorMessage = "Viettel trả về phản hồi tra cứu không nhận diện được.",
                    RawResponse = string.Empty,
                    DurationMs = durationMs
                };
            }

            var candidate = candidates[0];

            if (candidate.Kind == LookupCandidateKind.NotFound)
            {
                return new ViettelInvoiceLookupResultDto
                {
                    InvoiceHeadId = invoiceHeadId,
                    IsFound = false,
                    TransactionUuid = transactionUuid,
                    ErrorCode = "NOT_FOUND_DATA",
                    ErrorMessage = "Không tìm thấy hóa đơn trên Viettel theo transactionUuid.",
                    RawResponse = string.Empty,
                    DurationMs = durationMs
                };
            }

            var payload = candidate.Element;

            return new ViettelInvoiceLookupResultDto
            {
                InvoiceHeadId = invoiceHeadId,
                IsFound = true,
                TransactionUuid = transactionUuid,
                InvoiceNo =
                    ViettelClientHelper.GetFirstDirectString(
                        payload,
                        "invoiceNo",
                        "invoiceNumber",
                        "supplierInvoiceNo"),
                TransactionId =
                    ViettelClientHelper.GetFirstDirectString(
                        payload,
                        "transactionID",
                        "transactionId",
                        "transactionIDStr",
                        "invoiceId"),
                ReservationCode =
                    ViettelClientHelper.GetFirstDirectString(
                        payload,
                        "reservationCode",
                        "reservationNo"),
                CodeOfTax =
                    ViettelClientHelper.GetDirectString(
                        payload,
                        "codeOfTax"),
                IssueDateUtc =
                    TryParseIssueDateUtc(
                        ViettelClientHelper.GetFirstDirectString(
                            payload,
                            "issueDateStr",
                            "issueDate",
                            "invoiceIssuedDate")),
                RawResponse = string.Empty,
                DurationMs = durationMs
            };
        }
        catch
        {
            return new ViettelInvoiceLookupResultDto
            {
                InvoiceHeadId = invoiceHeadId,
                IsFound = false,
                TransactionUuid = transactionUuid,
                ErrorCode = "Viettel.LookupInvalidResponse",
                ErrorMessage = "Không đọc được phản hồi tra cứu UUID từ Viettel.",
                RawResponse = string.Empty,
                DurationMs = durationMs
            };
        }
    }

    private static IReadOnlyList<ProviderResponseContainer>
        GetLookupLocations(
            IReadOnlyList<ProviderResponseContainer> containers,
            out bool hasInvalidArrayPayload)
    {
        var locations = new List<ProviderResponseContainer>();
        hasInvalidArrayPayload = false;

        foreach (var container in containers)
        {
            if (container.Element.ValueKind == JsonValueKind.Object)
            {
                locations.Add(container);
                continue;
            }

            if (container.Element.ValueKind != JsonValueKind.Array)
            {
                hasInvalidArrayPayload = true;
                continue;
            }

            if (container.Element.GetArrayLength() == 0)
                continue;

            if (container.Element.GetArrayLength() != 1)
            {
                hasInvalidArrayPayload = true;
                continue;
            }

            var item = container.Element.EnumerateArray().First();

            if (item.ValueKind != JsonValueKind.Object)
            {
                hasInvalidArrayPayload = true;
                continue;
            }

            locations.Add(
                new ProviderResponseContainer(
                    container.Name,
                    item));
        }

        return locations;
    }

    private static LookupCandidateKind GetLookupCandidateKind(
        JsonElement container)
    {
        var hasInvoice =
            !string.IsNullOrWhiteSpace(
                ViettelClientHelper.GetFirstDirectString(
                    container,
                    "invoiceNo",
                    "invoiceNumber",
                    "supplierInvoiceNo"));
        var errorCode =
            ViettelClientHelper.GetFirstDirectString(
                container,
                "errorCode",
                "code");
        var message =
            ViettelClientHelper.GetFirstDirectString(
                container,
                "message",
                "description") ??
            string.Empty;
        var isNotFound =
            string.Equals(
                errorCode,
                "NOT_FOUND_DATA",
                StringComparison.OrdinalIgnoreCase) ||
            message.Contains(
                "NOT_FOUND_DATA",
                StringComparison.OrdinalIgnoreCase) ||
            message.Contains(
                "Không tìm thấy",
                StringComparison.OrdinalIgnoreCase);

        if (hasInvoice && isNotFound)
            return LookupCandidateKind.Conflicting;

        if (hasInvoice)
            return LookupCandidateKind.Found;

        return isNotFound
            ? LookupCandidateKind.NotFound
            : LookupCandidateKind.None;
    }

    private static bool IsAcceptedLookupControlCode(string code)
    {
        return ViettelClientHelper.IsExplicitViettelSuccessCode(code) ||
               string.Equals(
                   code,
                   "NOT_FOUND_DATA",
                   StringComparison.OrdinalIgnoreCase);
    }

    private enum LookupCandidateKind
    {
        None,
        Found,
        NotFound,
        Conflicting
    }

    private readonly record struct LookupCandidate(
        LookupCandidateKind Kind,
        JsonElement Element);

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
                RequestUrl = "Viettel:LookupByTransactionUuid",
                RequestBody = ViettelClientHelper.RedactedRequestSummary("LookupByTransactionUuid"),
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
            // Không để lỗi ghi log làm hỏng tra cứu.
            _logger.LogWarning(
                "Invoice integration-log persistence failed; provider result is preserved. Operation={Operation}; InvoiceHeadId={InvoiceHeadId}; ExceptionType={ExceptionType}",
                "LookupInvoice",
                invoiceHeadId,
                ex.GetType().Name);
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
