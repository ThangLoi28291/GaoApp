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

                    return Result<TestInvoiceProviderLoginResultDto>.Success(
                        new TestInvoiceProviderLoginResultDto
                        {
                            IsSuccess = false,
                            AuthModeName = "Basic Auth",
                            DurationMs = sw.ElapsedMilliseconds,
                            Message =
                                $"Basic Auth gọi getCustomFields thất bại. HTTP {(int)customFieldsResponse.StatusCode}. " +
                                $"Response: {ViettelClientHelper.Trim(customFieldsRaw, 700)}"
                        });
                }

                var customFieldsError = TryReadErrorMessage(customFieldsRaw);

                if (!string.IsNullOrWhiteSpace(customFieldsError))
                {
                    sw.Stop();

                    return Result<TestInvoiceProviderLoginResultDto>.Success(
                        new TestInvoiceProviderLoginResultDto
                        {
                            IsSuccess = false,
                            AuthModeName = "Basic Auth",
                            DurationMs = sw.ElapsedMilliseconds,
                            Message =
                                "Gọi getCustomFields được nhưng Viettel trả lỗi nghiệp vụ. " +
                                $"Lỗi: {customFieldsError}. Response: {ViettelClientHelper.Trim(customFieldsRaw, 700)}"
                        });
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
                return Result<TestInvoiceProviderLoginResultDto>.Success(
                    new TestInvoiceProviderLoginResultDto
                    {
                        IsSuccess = false,
                        AuthModeName = "Basic Auth",
                        DurationMs = sw.ElapsedMilliseconds,
                        Message =
                            $"Basic Auth gọi getInvoices thất bại. HTTP {(int)getInvoicesResponse.StatusCode}. " +
                            $"Response: {ViettelClientHelper.Trim(getInvoicesRaw, 700)}"
                    });
            }

            var getInvoicesError = TryReadErrorMessage(getInvoicesRaw);

            if (!string.IsNullOrWhiteSpace(getInvoicesError))
            {
                return Result<TestInvoiceProviderLoginResultDto>.Success(
                    new TestInvoiceProviderLoginResultDto
                    {
                        IsSuccess = false,
                        AuthModeName = "Basic Auth",
                        DurationMs = sw.ElapsedMilliseconds,
                        Message =
                            "Gọi getInvoices được nhưng Viettel trả lỗi nghiệp vụ. " +
                            $"Lỗi: {getInvoicesError}. Response: {ViettelClientHelper.Trim(getInvoicesRaw, 700)}"
                    });
            }

            var totalRows =
                TryReadIntProperty(getInvoicesRaw, "totalRows") ??
                TryReadIntProperty(getInvoicesRaw, "totalRow");

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
        catch (TaskCanceledException)
        {
            sw.Stop();

            return Result<TestInvoiceProviderLoginResultDto>.Success(
                new TestInvoiceProviderLoginResultDto
                {
                    IsSuccess = false,
                    AuthModeName = "Basic Auth",
                    DurationMs = sw.ElapsedMilliseconds,
                    Message = "Basic Auth test timeout. Kiểm tra mạng, BaseUrl hoặc server Viettel."
                });
        }
        catch (Exception ex)
        {
            sw.Stop();

            return Result<TestInvoiceProviderLoginResultDto>.Success(
                new TestInvoiceProviderLoginResultDto
                {
                    IsSuccess = false,
                    AuthModeName = "Basic Auth",
                    DurationMs = sw.ElapsedMilliseconds,
                    Message = $"Lỗi khi test Basic Auth Viettel: {ex.Message}"
                });
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
                return Result<TestInvoiceProviderLoginResultDto>.Success(
                    new TestInvoiceProviderLoginResultDto
                    {
                        IsSuccess = false,
                        AuthModeName = "Token Login",
                        DurationMs = sw.ElapsedMilliseconds,
                        Message =
                            $"Token Login thất bại. HTTP {(int)response.StatusCode}. " +
                            $"Response: {ViettelClientHelper.Trim(raw, 700)}"
                    });
            }

            string? token = null;

            if (!string.IsNullOrWhiteSpace(raw))
            {
                using var doc = JsonDocument.Parse(raw);

                token =
                    ViettelClientHelper.FindStringProperty(doc.RootElement, "access_token") ??
                    ViettelClientHelper.FindStringProperty(doc.RootElement, "accessToken") ??
                    ViettelClientHelper.FindStringProperty(doc.RootElement, "token");
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                return Result<TestInvoiceProviderLoginResultDto>.Success(
                    new TestInvoiceProviderLoginResultDto
                    {
                        IsSuccess = false,
                        AuthModeName = "Token Login",
                        DurationMs = sw.ElapsedMilliseconds,
                        Message =
                            $"Token Login có phản hồi nhưng không tìm thấy access_token. Response: {ViettelClientHelper.Trim(raw, 700)}"
                    });
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
        catch (TaskCanceledException)
        {
            sw.Stop();

            return Result<TestInvoiceProviderLoginResultDto>.Success(
                new TestInvoiceProviderLoginResultDto
                {
                    IsSuccess = false,
                    AuthModeName = "Token Login",
                    DurationMs = sw.ElapsedMilliseconds,
                    Message = "Token Login Viettel timeout. Kiểm tra mạng, BaseUrl hoặc server Viettel."
                });
        }
        catch (Exception ex)
        {
            sw.Stop();

            return Result<TestInvoiceProviderLoginResultDto>.Success(
                new TestInvoiceProviderLoginResultDto
                {
                    IsSuccess = false,
                    AuthModeName = "Token Login",
                    DurationMs = sw.ElapsedMilliseconds,
                    Message = $"Lỗi khi Token Login Viettel: {ex.Message}"
                });
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

    private static string? TryReadErrorMessage(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(raw);

            var errorCode =
                ViettelClientHelper.FindStringProperty(doc.RootElement, "errorCode") ??
                ViettelClientHelper.FindStringProperty(doc.RootElement, "code");

            var description =
                ViettelClientHelper.FindStringProperty(doc.RootElement, "description") ??
                ViettelClientHelper.FindStringProperty(doc.RootElement, "message");

            if (!ViettelClientHelper.IsViettelSuccessCode(errorCode))
            {
                return string.IsNullOrWhiteSpace(description)
                    ? errorCode
                    : $"{errorCode} - {description}";
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static int? TryReadIntProperty(
        string? raw,
        string propertyName)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(raw);

            var value = ViettelClientHelper.FindStringProperty(
                doc.RootElement,
                propertyName);

            if (int.TryParse(value, out var number))
                return number;

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string MaskToken(string token)
    {
        if (token.Length <= 12)
            return "***";

        return token[..6] + "..." + token[^6..];
    }
}