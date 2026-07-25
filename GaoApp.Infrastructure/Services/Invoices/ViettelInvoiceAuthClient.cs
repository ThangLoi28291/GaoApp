using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Domain.Enums;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace GaoApp.Infrastructure.Services.Invoices;

public class ViettelInvoiceAuthClient : IViettelInvoiceAuthClient
{
    private readonly HttpClient _httpClient;

    public ViettelInvoiceAuthClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<Result<TestInvoiceProviderLoginResultDto>> TestConnectionAsync(
        string baseUrl,
        string username,
        string password,
        InvoiceProviderAuthMode authMode,
        string supplierTaxCode,
        string invoiceType,
        string templateCode,
        string invoiceSeries,
        CancellationToken ct = default)
    {
        if (authMode == InvoiceProviderAuthMode.BasicAuth)
        {
            return await TestBasicAuthAsync(
                baseUrl,
                username,
                password,
                supplierTaxCode,
                invoiceType,
                templateCode,
                invoiceSeries,
                ct);
        }

        return await TestTokenLoginAsync(
            baseUrl,
            username,
            password,
            ct);
    }

    private async Task<Result<TestInvoiceProviderLoginResultDto>> TestBasicAuthAsync(
        string baseUrl,
        string username,
        string password,
        string supplierTaxCode,
        string invoiceType,
        string templateCode,
        string invoiceSeries,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        var validate = ValidateCommon(
            baseUrl,
            username,
            password);

        if (!validate.IsSuccess)
            return Result<TestInvoiceProviderLoginResultDto>.Failure(validate.Error!);

        if (string.IsNullOrWhiteSpace(supplierTaxCode))
        {
            return Result<TestInvoiceProviderLoginResultDto>.Failure(
                Error.Validation(
                    "InvoiceProvider.SupplierTaxCodeRequired",
                    "MST phát hành không được trống."));
        }

        if (string.IsNullOrWhiteSpace(templateCode))
        {
            return Result<TestInvoiceProviderLoginResultDto>.Failure(
                Error.Validation(
                    "InvoiceProvider.TemplateCodeRequired",
                    "TemplateCode không được trống."));
        }

        if (string.IsNullOrWhiteSpace(invoiceSeries))
        {
            return Result<TestInvoiceProviderLoginResultDto>.Failure(
                Error.Validation(
                    "InvoiceProvider.InvoiceSeriesRequired",
                    "Ký hiệu hóa đơn không được trống."));
        }

        try
        {
            var normalizedBaseUrl = ViettelClientHelper.NormalizeBaseUrl(baseUrl);

            var authHeader = ViettelClientHelper.BuildBasicAuthHeader(
                username,
                password);

            // =====================================================
            // TEST 1: getCustomFields
            // API nhẹ, không phát hành hóa đơn.
            // Dùng để kiểm tra Basic Auth + MST + TemplateCode.
            // =====================================================
            var customFieldsUrl =
                $"{normalizedBaseUrl}/InvoiceAPI/InvoiceWS/getCustomFields" +
                $"?taxCode={Uri.EscapeDataString(supplierTaxCode.Trim())}" +
                $"&templateCode={Uri.EscapeDataString(templateCode.Trim())}";

            using (var customFieldsRequest = new HttpRequestMessage(HttpMethod.Get, customFieldsUrl))
            {
                customFieldsRequest.Headers.Accept.Clear();
                customFieldsRequest.Headers.Accept.ParseAdd("application/json");
                customFieldsRequest.Headers.Authorization = authHeader;

                using var customFieldsResponse = await _httpClient.SendAsync(
                    customFieldsRequest,
                    ct);

                var customFieldsRaw = await customFieldsResponse.Content.ReadAsStringAsync(ct);

                if (!customFieldsResponse.IsSuccessStatusCode)
                {
                    sw.Stop();

                    return Result<TestInvoiceProviderLoginResultDto>.Failure(
                        Error.Validation(
                            "Viettel.BasicAuthHttpFailed",
                            $"Basic Auth gọi getCustomFields thất bại. HTTP {(int)customFieldsResponse.StatusCode}."));
                }

                var customFieldsResult =
                    ParseCustomFieldsResponse(customFieldsRaw);

                if (!customFieldsResult.IsSuccess)
                {
                    sw.Stop();

                    return Result<TestInvoiceProviderLoginResultDto>.Failure(
                        Error.Validation(
                            customFieldsResult.ErrorCode ??
                                "Viettel.BasicAuthInvalidResponse",
                            customFieldsResult.ErrorMessage ??
                                "Viettel trả về phản hồi getCustomFields không hợp lệ."));
                }
            }

            // =====================================================
            // TEST 2: getInvoices
            // Dùng để kiểm tra quyền đọc danh sách hóa đơn.
            // Không phát hành hóa đơn.
            // =====================================================
            var today = DateTime.Now.Date;
            var startDate = today.AddDays(-30).ToString("yyyy-MM-dd");
            var endDate = today.ToString("yyyy-MM-dd");

            var getInvoicesUrl =
                $"{normalizedBaseUrl}/InvoiceAPI/InvoiceUtilsWS/getInvoices/{Uri.EscapeDataString(supplierTaxCode.Trim())}";

            using var getInvoicesRequest = new HttpRequestMessage(
                HttpMethod.Post,
                getInvoicesUrl);

            getInvoicesRequest.Headers.Accept.Clear();
            getInvoicesRequest.Headers.Accept.ParseAdd("application/json");
            getInvoicesRequest.Headers.Authorization = authHeader;

            var getInvoicesBody = new
            {
                startDate,
                endDate,
                invoiceType = string.IsNullOrWhiteSpace(invoiceType)
                    ? "1"
                    : invoiceType.Trim(),
                rowPerPage = 5,
                pageNum = 1,
                templateCode = templateCode.Trim(),
                invoiceSeri = invoiceSeries.Trim(),
                getAll = true
            };

            var getInvoicesBodyJson = ViettelClientHelper.SerializeJson(getInvoicesBody);

            getInvoicesRequest.Content = new StringContent(
                getInvoicesBodyJson,
                Encoding.UTF8,
                "application/json");

            using var getInvoicesResponse = await _httpClient.SendAsync(
                getInvoicesRequest,
                ct);

            var getInvoicesRaw = await getInvoicesResponse.Content.ReadAsStringAsync(ct);

            sw.Stop();

            if (!getInvoicesResponse.IsSuccessStatusCode)
            {
                return Result<TestInvoiceProviderLoginResultDto>.Failure(
                    Error.Validation(
                        "Viettel.BasicAuthHttpFailed",
                        $"Basic Auth gọi getInvoices thất bại. HTTP {(int)getInvoicesResponse.StatusCode}."));
            }

            var getInvoicesResult =
                ParseBasicAuthGetInvoicesResponse(getInvoicesRaw);

            if (!getInvoicesResult.IsSuccess)
            {
                return Result<TestInvoiceProviderLoginResultDto>.Failure(
                    Error.Validation(
                        getInvoicesResult.ErrorCode ??
                            "Viettel.BasicAuthInvalidResponse",
                        getInvoicesResult.ErrorMessage ??
                            "Viettel trả về phản hồi getInvoices không hợp lệ."));
            }

            var totalRows = getInvoicesResult.TotalRows;

            return Result<TestInvoiceProviderLoginResultDto>.Success(
                new TestInvoiceProviderLoginResultDto
                {
                    IsSuccess = true,
                    AuthModeName = "Basic Auth",
                    DurationMs = sw.ElapsedMilliseconds,
                    TotalRows = totalRows,
                    Message =
                        "Kết nối Viettel SInvoice bằng Basic Auth thành công. " +
                        $"Đã test getCustomFields và getInvoices. Tổng hóa đơn đọc được: {(totalRows.HasValue ? totalRows.Value.ToString("N0") : "không xác định")}."
                });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            sw.Stop();

            return Result<TestInvoiceProviderLoginResultDto>.Failure(
                Error.Validation(
                    "Viettel.BasicAuthTimeout",
                    "Basic Auth test timeout. Kiểm tra mạng, BaseUrl hoặc server Viettel."));
        }
        catch (Exception)
        {
            sw.Stop();

            return Result<TestInvoiceProviderLoginResultDto>.Failure(
                Error.Validation(
                    "Viettel.BasicAuthTransportFailed",
                    "Không gọi được dịch vụ kiểm tra Basic Auth Viettel."));
        }
    }

    private async Task<Result<TestInvoiceProviderLoginResultDto>> TestTokenLoginAsync(
        string baseUrl,
        string username,
        string password,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        var validate = ValidateCommon(
            baseUrl,
            username,
            password);

        if (!validate.IsSuccess)
            return Result<TestInvoiceProviderLoginResultDto>.Failure(validate.Error!);

        var normalizedBaseUrl = ViettelClientHelper.NormalizeBaseUrl(baseUrl);
        var url = $"{normalizedBaseUrl}/auth/login";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);

            request.Headers.Accept.Clear();
            request.Headers.Accept.ParseAdd("application/json");

            var body = new
            {
                username = username.Trim(),
                password = password
            };

            var bodyJson = ViettelClientHelper.SerializeJson(body);

            request.Content = new StringContent(
                bodyJson,
                Encoding.UTF8,
                "application/json");

            using var response = await _httpClient.SendAsync(request, ct);

            var raw = await response.Content.ReadAsStringAsync(ct);

            sw.Stop();

            if (!response.IsSuccessStatusCode)
            {
                return Result<TestInvoiceProviderLoginResultDto>.Failure(
                    Error.Validation(
                        "Viettel.TokenLoginHttpFailed",
                        $"Token Login thất bại. HTTP {(int)response.StatusCode}."));
            }

            string? token = null;

            if (!string.IsNullOrWhiteSpace(raw))
            {
                var jsonValidation =
                    ViettelClientHelper.TryParseStrictProviderJson(
                        raw,
                        out var document);

                if (jsonValidation != ProviderJsonValidationFailure.None ||
                    document is null)
                {
                    return Result<TestInvoiceProviderLoginResultDto>.Failure(
                        Error.Validation(
                            "Viettel.TokenLoginInvalidResponse",
                            "Không đọc được phản hồi Token Login Viettel."));
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
                    return Result<TestInvoiceProviderLoginResultDto>.Failure(
                        Error.Validation(
                            "Viettel.TokenLoginInvalidResponse",
                            "Viettel trả về envelope Token Login không hợp lệ."));
                }

                if (containers.Any(
                        container =>
                            ViettelClientHelper.HasDirectFailureMarker(
                                container.Element,
                                ViettelClientHelper.IsExplicitViettelSuccessCode)))
                {
                    return Result<TestInvoiceProviderLoginResultDto>.Failure(
                        Error.Validation(
                            "Viettel.TokenLoginBusinessFailed",
                            "Viettel trả lỗi Token Login."));
                }

                var candidates = containers
                    .Select(
                        container =>
                            ViettelClientHelper.GetFirstDirectString(
                                container.Element,
                                "access_token",
                                "accessToken",
                                "token"))
                    .Where(
                        value =>
                            !string.IsNullOrWhiteSpace(value))
                    .ToList();

                if (candidates.Count > 1)
                {
                    return Result<TestInvoiceProviderLoginResultDto>.Failure(
                        Error.Validation(
                            "Viettel.TokenLoginConflictingResponse",
                            "Viettel trả về phản hồi mâu thuẫn hoặc không đủ điều kiện xác nhận."));
                }

                if (candidates.Count == 1)
                {
                    token = candidates[0];
                }
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                return Result<TestInvoiceProviderLoginResultDto>.Failure(
                    Error.Validation(
                        "Viettel.TokenMissing",
                        "Token Login có phản hồi nhưng không tìm thấy access_token."));
            }

            return Result<TestInvoiceProviderLoginResultDto>.Success(
                new TestInvoiceProviderLoginResultDto
                {
                    IsSuccess = true,
                    AuthModeName = "Token Login",
                    DurationMs = sw.ElapsedMilliseconds,
                    Message = "Token Login Viettel SInvoice thành công.",
                    TokenPreview = MaskToken(token)
                });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            sw.Stop();

            return Result<TestInvoiceProviderLoginResultDto>.Failure(
                Error.Validation(
                    "Viettel.TokenLoginTimeout",
                    "Token Login Viettel timeout. Kiểm tra mạng, BaseUrl hoặc server Viettel."));
        }
        catch (JsonException)
        {
            sw.Stop();

            return Result<TestInvoiceProviderLoginResultDto>.Failure(
                Error.Validation(
                    "Viettel.TokenLoginInvalidResponse",
                    "Không đọc được phản hồi Token Login Viettel."));
        }
        catch (Exception)
        {
            sw.Stop();

            return Result<TestInvoiceProviderLoginResultDto>.Failure(
                Error.Validation(
                    "Viettel.TokenLoginTransportFailed",
                    "Không gọi được dịch vụ Token Login Viettel."));
        }
    }

    private static Result<bool> ValidateCommon(
        string baseUrl,
        string username,
        string password)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return Result<bool>.Failure(
                Error.Validation(
                    "InvoiceProvider.BaseUrlRequired",
                    "BaseUrl không được trống."));
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            return Result<bool>.Failure(
                Error.Validation(
                    "InvoiceProvider.UsernameRequired",
                    "Username không được trống."));
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            return Result<bool>.Failure(
                Error.Validation(
                    "InvoiceProvider.PasswordRequired",
                    "Password không được trống."));
        }

        return Result<bool>.Success(true);
    }

    private static BasicAuthResponseParseResult ParseCustomFieldsResponse(
        string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return InvalidBasicAuthResponse(
                "Viettel.BasicAuthInvalidResponse",
                "Viettel không trả dữ liệu getCustomFields.");
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
                return InvalidBasicAuthResponse(
                    "Viettel.BasicAuthInvalidResponse",
                    "Không đọc được phản hồi getCustomFields từ Viettel.");
            }

            using var strictDocument = document;
            var root = strictDocument.RootElement;
            var containers =
                ViettelClientHelper.GetDirectRecognizedContainers(
                    root,
                    allowResultArray: true,
                    allowDataArray: false,
                    out var hasInvalidEnvelope);

            if (hasInvalidEnvelope)
            {
                return InvalidBasicAuthResponse(
                    "Viettel.BasicAuthInvalidResponse",
                    "Viettel trả về envelope getCustomFields không hợp lệ.");
            }

            if (containers.Any(
                    container =>
                        ViettelClientHelper.HasDirectFailureMarker(
                            container.Element,
                            ViettelClientHelper.IsExplicitViettelSuccessCode)))
            {
                return InvalidBasicAuthResponse(
                    "Viettel.BasicAuthBusinessFailed",
                    "Viettel trả lỗi getCustomFields.");
            }

            var payloadCandidates = containers
                .Where(IsCustomFieldsPayloadCandidate)
                .ToList();
            var candidates = payloadCandidates.Count > 0
                ? payloadCandidates
                : containers
                    .Where(
                        container =>
                            HasExplicitSuccessControlCode(
                                container.Element))
                    .ToList();

            if (candidates.Count > 1)
            {
                return InvalidBasicAuthResponse(
                    "Viettel.BasicAuthConflictingResponse",
                    "Viettel trả về phản hồi mâu thuẫn hoặc không đủ điều kiện xác nhận.");
            }

            if (candidates.Count == 0)
            {
                return InvalidBasicAuthResponse(
                    "Viettel.BasicAuthAmbiguousResponse",
                    "Viettel trả về phản hồi getCustomFields không nhận diện được.");
            }

            return new BasicAuthResponseParseResult
            {
                IsSuccess = true
            };
        }
        catch
        {
            return InvalidBasicAuthResponse(
                "Viettel.BasicAuthInvalidResponse",
                "Không đọc được phản hồi getCustomFields từ Viettel.");
        }
    }

    private static bool IsCustomFieldsPayloadCandidate(
        ProviderResponseContainer container)
    {
        if (container.Name.Equals(
                "result",
                StringComparison.OrdinalIgnoreCase))
        {
            return container.Element.ValueKind is
                JsonValueKind.Object or JsonValueKind.Array;
        }

        if (container.Name.Equals(
                "data",
                StringComparison.OrdinalIgnoreCase))
        {
            return HasDirectResultShape(container.Element);
        }

        return false;
    }

    private static bool HasDirectResultShape(JsonElement element)
    {
        return ViettelClientHelper.GetUniqueDirectProperty(
                   element,
                   "result",
                   out var resultElement) ==
               DirectPropertyLookupResult.Found &&
               resultElement.ValueKind is
                   JsonValueKind.Array or JsonValueKind.Object;
    }

    private static BasicAuthResponseParseResult
        ParseBasicAuthGetInvoicesResponse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return InvalidBasicAuthResponse(
                "Viettel.BasicAuthInvalidResponse",
                "Viettel không trả dữ liệu getInvoices.");
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
                return InvalidBasicAuthResponse(
                    "Viettel.BasicAuthInvalidResponse",
                    "Không đọc được phản hồi getInvoices từ Viettel.");
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
                return InvalidBasicAuthResponse(
                    "Viettel.BasicAuthInvalidResponse",
                    "Viettel trả về envelope getInvoices không hợp lệ.");
            }

            if (containers.Any(
                    container =>
                        ViettelClientHelper.HasDirectFailureMarker(
                            container.Element,
                            ViettelClientHelper.IsExplicitViettelSuccessCode)))
            {
                return InvalidBasicAuthResponse(
                    "Viettel.BasicAuthBusinessFailed",
                    "Viettel trả lỗi getInvoices.");
            }

            var candidates = containers
                .Where(
                    container =>
                        HasDirectListShape(container.Element))
                .ToList();

            if (candidates.Count > 1)
            {
                return InvalidBasicAuthResponse(
                    "Viettel.BasicAuthConflictingResponse",
                    "Viettel trả về phản hồi mâu thuẫn hoặc không đủ điều kiện xác nhận.");
            }

            if (candidates.Count == 0)
            {
                return InvalidBasicAuthResponse(
                    "Viettel.BasicAuthAmbiguousResponse",
                    "Viettel trả về phản hồi getInvoices không nhận diện được.");
            }

            var responseContainer = candidates[0].Element;
            var hasInvoices =
                ViettelClientHelper.GetUniqueDirectProperty(
                    responseContainer,
                    "invoices",
                    out var invoicesElement) ==
                DirectPropertyLookupResult.Found;

            if (hasInvoices &&
                invoicesElement.ValueKind != JsonValueKind.Array)
            {
                return InvalidBasicAuthResponse(
                    "Viettel.BasicAuthInvalidResponse",
                    "Viettel trả về trường invoices không hợp lệ.");
            }

            if (hasInvoices)
            {
                foreach (var item in invoicesElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object ||
                        !HasStableInvoiceIdentity(item))
                    {
                        return InvalidBasicAuthResponse(
                            "Viettel.BasicAuthInvalidResponse",
                            "Viettel trả về phần tử hóa đơn thiếu định danh ổn định.");
                    }
                }
            }

            var hasTotalRows =
                ViettelClientHelper.GetUniqueDirectProperty(
                    responseContainer,
                    "totalRows",
                    out var totalRowsElement) ==
                DirectPropertyLookupResult.Found;
            var hasTotalRow =
                !hasTotalRows &&
                ViettelClientHelper.GetUniqueDirectProperty(
                    responseContainer,
                    "totalRow",
                    out totalRowsElement) ==
                DirectPropertyLookupResult.Found;
            var hasTotal = hasTotalRows || hasTotalRow;
            int? totalRows = null;

            if (hasTotal)
            {
                var totalRowsText = totalRowsElement.ValueKind switch
                {
                    JsonValueKind.Number => totalRowsElement.ToString(),
                    JsonValueKind.String => totalRowsElement.GetString(),
                    _ => null
                };

                if (!int.TryParse(totalRowsText, out var parsedTotalRows) ||
                    parsedTotalRows < 0)
                {
                    return InvalidBasicAuthResponse(
                        "Viettel.BasicAuthInvalidResponse",
                        "Viettel trả về tổng số hóa đơn không hợp lệ.");
                }

                totalRows = parsedTotalRows;
            }

            if (!hasInvoices && !hasTotal)
            {
                return InvalidBasicAuthResponse(
                    "Viettel.BasicAuthAmbiguousResponse",
                    "Viettel trả về phản hồi getInvoices không nhận diện được.");
            }

            if (!hasInvoices && totalRows > 0)
            {
                return InvalidBasicAuthResponse(
                    "Viettel.BasicAuthAmbiguousResponse",
                    "Viettel trả về tổng số hóa đơn nhưng thiếu danh sách hóa đơn.");
            }

            return new BasicAuthResponseParseResult
            {
                IsSuccess = true,
                TotalRows = totalRows ??
                    invoicesElement.GetArrayLength()
            };
        }
        catch
        {
            return InvalidBasicAuthResponse(
                "Viettel.BasicAuthInvalidResponse",
                "Không đọc được phản hồi getInvoices từ Viettel.");
        }
    }

    private static bool HasDirectListShape(JsonElement element)
    {
        return ViettelClientHelper.GetUniqueDirectProperty(
                   element,
                   "invoices",
                   out _) == DirectPropertyLookupResult.Found ||
               ViettelClientHelper.GetUniqueDirectProperty(
                   element,
                   "totalRows",
                   out _) == DirectPropertyLookupResult.Found ||
               ViettelClientHelper.GetUniqueDirectProperty(
                   element,
                   "totalRow",
                   out _) == DirectPropertyLookupResult.Found;
    }

    private static bool HasStableInvoiceIdentity(JsonElement item)
    {
        return !string.IsNullOrWhiteSpace(
                   ViettelClientHelper.GetDirectString(
                       item,
                       "invoiceNo")) ||
               !string.IsNullOrWhiteSpace(
                   ViettelClientHelper.GetDirectString(
                       item,
                       "invoiceId")) ||
               !string.IsNullOrWhiteSpace(
                   ViettelClientHelper.GetDirectString(
                       item,
                       "transactionUuid"));
    }

    private static bool HasExplicitSuccessControlCode(JsonElement container)
    {
        return ViettelClientHelper.IsExplicitViettelSuccessCode(
            ViettelClientHelper.GetFirstDirectString(
                container,
                "errorCode",
                "code"));
    }

    private static BasicAuthResponseParseResult InvalidBasicAuthResponse(
        string errorCode,
        string errorMessage) =>
        new()
        {
            IsSuccess = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };

    private static string MaskToken(string token)
    {
        return "***";
    }

    private sealed class BasicAuthResponseParseResult
    {
        public bool IsSuccess { get; set; }

        public int? TotalRows { get; set; }

        public string? ErrorCode { get; set; }

        public string? ErrorMessage { get; set; }
    }
}
