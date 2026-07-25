using System.Net;
using System.Text.Json;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GaoApp.Infrastructure.Services.Invoices;

public class VietQrTaxCodeLookupService : ITaxCodeLookupService
{
    private readonly HttpClient _httpClient;
    private readonly TaxCodeLookupOptions _options;
    private readonly ILogger<VietQrTaxCodeLookupService> _logger;

    public VietQrTaxCodeLookupService(
        HttpClient httpClient,
        IOptions<TaxCodeLookupOptions> options,
        ILogger<VietQrTaxCodeLookupService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Hàm cũ giữ lại để tương thích code cũ.
    /// </summary>
    public Task<Result<TaxCodeLookupResultDto>> LookupAsync(
        string taxCode,
        CancellationToken ct = default)
    {
        return LookupBusinessAsync(taxCode, ct);
    }

    /// <summary>
    /// Tra cứu doanh nghiệp từ VietQR.
    /// Không ép MST 10/13.
    /// Không xóa dấu gạch ngang.
    /// </summary>
    public async Task<Result<TaxCodeLookupResultDto>> LookupBusinessAsync(
        string taxCode,
        CancellationToken ct = default)
    {
        var originalTaxCode = NormalizeTaxCodeForLookup(taxCode);

        if (!_options.Enabled)
        {
            return Result<TaxCodeLookupResultDto>.Failure(
                Error.Validation(
                    "TaxCodeLookup.Disabled",
                    "Chức năng tra cứu MST ngoài đang tắt."));
        }

        if (string.IsNullOrWhiteSpace(originalTaxCode))
        {
            return Result<TaxCodeLookupResultDto>.Failure(
                Error.Validation(
                    "TaxCodeLookup.TaxCodeRequired",
                    "Vui lòng nhập MST/mã định danh để tra cứu."));
        }

        if (!IsSafeLookupTaxCode(originalTaxCode))
        {
            return Result<TaxCodeLookupResultDto>.Failure(
                Error.Validation(
                    "TaxCodeLookup.InvalidTaxCode",
                    "MST/mã định danh chỉ cho phép chữ, số, dấu gạch ngang và tối đa 20 ký tự."));
        }

        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? "https://api.vietqr.io"
            : _options.BaseUrl.Trim().TrimEnd('/');

        var timeoutSeconds = _options.TimeoutSeconds <= 0
            ? 10
            : Math.Min(
                _options.TimeoutSeconds,
                ExternalHttpResilienceOptions.MaximumTimeoutSeconds);

        var candidates = BuildLookupCandidates(originalTaxCode);

        string? lastErrorMessage = null;

        foreach (var lookupTaxCode in candidates)
        {
            var url = $"{baseUrl}/v2/business/{Uri.EscapeDataString(lookupTaxCode)}";

            for (var retry = 0;
                 retry <= ExternalHttpResilienceOptions.SafeGetTransientRetryCount;
                 retry++)
            {
                try
                {
                    using var timeoutCts =
                        CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                    using var request = new HttpRequestMessage(HttpMethod.Get, url);

                    request.Headers.Accept.Clear();
                    request.Headers.Accept.ParseAdd("application/json");

                    using var response = await _httpClient.SendAsync(
                        request,
                        timeoutCts.Token);

                    var raw = await response.Content.ReadAsStringAsync(timeoutCts.Token);

                    if (IsTransientStatusCode(response.StatusCode) &&
                        retry < ExternalHttpResilienceOptions.SafeGetTransientRetryCount)
                    {
                        await DelayBeforeSafeGetRetryAsync(ct);
                        continue;
                    }

                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        return Result<TaxCodeLookupResultDto>.Failure(
                            Error.Failure(
                                "API tra cứu MST đang bị giới hạn lượt gọi. Vui lòng thử lại sau."));
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        lastErrorMessage =
                            $"Tra cứu MST thất bại. HTTP {(int)response.StatusCode}.";

                        if (response.StatusCode == HttpStatusCode.NotFound)
                            break;

                        return Result<TaxCodeLookupResultDto>.Failure(
                            Error.Failure(lastErrorMessage));
                    }

                    var parsed = ParseVietQrResponse(
                        originalTaxCode,
                        raw,
                        out var hasInvalidProviderResponse);

                    if (hasInvalidProviderResponse)
                    {
                        return Result<TaxCodeLookupResultDto>.Failure(
                            Error.Validation(
                                "TaxCodeLookup.InvalidProviderResponse",
                                "VietQR trả về phản hồi không hợp lệ."));
                    }

                    if (parsed != null)
                    {
                        return Result<TaxCodeLookupResultDto>.Success(parsed);
                    }

                    lastErrorMessage =
                        "Không tìm thấy thông tin doanh nghiệp từ VietQR.";
                    break;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    if (retry < ExternalHttpResilienceOptions.SafeGetTransientRetryCount)
                    {
                        await DelayBeforeSafeGetRetryAsync(ct);
                        continue;
                    }

                    return Result<TaxCodeLookupResultDto>.Failure(
                        Error.Failure("Tra cứu VietQR quá thời gian chờ."));
                }
                catch (HttpRequestException ex)
                {
                    if (retry < ExternalHttpResilienceOptions.SafeGetTransientRetryCount)
                    {
                        await DelayBeforeSafeGetRetryAsync(ct);
                        continue;
                    }

                    LogProviderException(ex);

                    return Result<TaxCodeLookupResultDto>.Failure(
                        Error.Failure("Không gọi được VietQR."));
                }
                catch (Exception ex)
                {
                    LogProviderException(ex);

                    return Result<TaxCodeLookupResultDto>.Failure(
                        Error.Failure("Không gọi được VietQR."));
                }
            }
        }

        return Result<TaxCodeLookupResultDto>.Failure(
            Error.NotFound(
                string.IsNullOrWhiteSpace(lastErrorMessage)
                    ? "Không tìm thấy thông tin doanh nghiệp từ API tra MST."
                    : lastErrorMessage));
    }

    private static bool IsTransientStatusCode(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.RequestTimeout ||
        statusCode == HttpStatusCode.TooManyRequests ||
        (int)statusCode >= 500;

    private static async Task DelayBeforeSafeGetRetryAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        await Task.Delay(
            ExternalHttpResilienceOptions.SafeGetTransientRetryDelay,
            ct);
    }

    private void LogProviderException(Exception exception)
    {
        _logger.LogWarning(
            "VietQR tax code lookup failed. ExceptionType={ExceptionType}",
            exception.GetType().Name);
    }

    private static List<string> BuildLookupCandidates(string taxCode)
    {
        taxCode = NormalizeTaxCodeForLookup(taxCode);

        var results = new List<string>();

        if (!string.IsNullOrWhiteSpace(taxCode))
            results.Add(taxCode);

        // Một số API có thể lưu MST chi nhánh dạng bỏ dấu "-".
        // Ví dụ người dùng nhập 0312770607-001, thử thêm 0312770607001.
        var noDash = taxCode.Replace("-", "");

        if (!string.IsNullOrWhiteSpace(noDash) &&
            !results.Contains(noDash, StringComparer.OrdinalIgnoreCase))
        {
            results.Add(noDash);
        }

        return results;
    }

    private static TaxCodeLookupResultDto? ParseVietQrResponse(
        string originalTaxCode,
        string raw,
        out bool hasInvalidProviderResponse)
    {
        hasInvalidProviderResponse = false;

        if (string.IsNullOrWhiteSpace(raw))
        {
            hasInvalidProviderResponse = true;
            return null;
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
                hasInvalidProviderResponse = true;
                return null;
            }

            using var strictDocument = document;
            var root = strictDocument.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                hasInvalidProviderResponse = true;
                return null;
            }

            var code =
                ViettelClientHelper.GetFirstDirectString(
                    root,
                    "code",
                    "errorCode");

            var desc =
                ViettelClientHelper.GetFirstDirectString(
                    root,
                    "desc",
                    "message",
                    "description");

            // VietQR thường trả code "00" khi thành công.
            if (!string.IsNullOrWhiteSpace(code) &&
                !code.Equals("00", StringComparison.OrdinalIgnoreCase) &&
                !code.Equals("200", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            JsonElement data = root;
            var dataLookup =
                ViettelClientHelper.GetUniqueDirectProperty(
                    root,
                    "data",
                    out var dataElement);

            if (dataLookup == DirectPropertyLookupResult.Found)
            {
                if (dataElement.ValueKind != JsonValueKind.Object)
                {
                    hasInvalidProviderResponse = true;
                    return null;
                }

                data = dataElement;
            }

            var name =
                ViettelClientHelper.GetFirstDirectString(
                    data,
                    "name",
                    "companyName",
                    "businessName",
                    "legalName");

            var internationalName =
                ViettelClientHelper.GetFirstDirectString(
                    data,
                    "internationalName",
                    "international_name");

            var shortName =
                ViettelClientHelper.GetFirstDirectString(
                    data,
                    "shortName",
                    "short_name");

            var address =
                ViettelClientHelper.GetFirstDirectString(
                    data,
                    "address",
                    "companyAddress",
                    "businessAddress");

            var representative =
                ViettelClientHelper.GetFirstDirectString(
                    data,
                    "representativeName",
                    "legalRepresentative",
                    "ownerName");

            var returnedTaxCode =
                ViettelClientHelper.GetFirstDirectString(
                    data,
                    "id",
                    "taxCode",
                    "mst");

            if (string.IsNullOrWhiteSpace(name) &&
                string.IsNullOrWhiteSpace(address))
            {
                return null;
            }

            return new TaxCodeLookupResultDto
            {
                IsFound = true,

                // Quan trọng:
                // Giữ lại MST người dùng nhập ban đầu để không làm mất dấu "-".
                // VietQR chỉ là nguồn gợi ý tên/địa chỉ.
                TaxCode = originalTaxCode,

                CompanyName = NormalizeText(name),
                InternationalName = NormalizeText(internationalName),
                ShortName = NormalizeText(shortName),
                Address = NormalizeText(address),
                LegalRepresentative = NormalizeText(representative),
                Source = "vietqr",
                RawCode = code,
                RawMessage = desc
            };
        }
        catch
        {
            hasInvalidProviderResponse = true;
            return null;
        }
    }

    private static string NormalizeTaxCodeForLookup(string? taxCode)
    {
        taxCode = (taxCode ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(taxCode))
            return string.Empty;

        // Không xóa dấu "-".
        // Không ép 10/13 số.
        return taxCode
            .Replace(" ", "")
            .Replace(".", "")
            .Trim()
            .ToUpperInvariant();
    }

    private static bool IsSafeLookupTaxCode(string taxCode)
    {
        taxCode = NormalizeTaxCodeForLookup(taxCode);

        if (string.IsNullOrWhiteSpace(taxCode))
            return false;

        if (taxCode.Length > 20)
            return false;

        return taxCode.All(x => char.IsLetterOrDigit(x) || x == '-');
    }

    private static string? NormalizeText(string? value)
    {
        value = (value ?? string.Empty).Trim();

        return string.IsNullOrWhiteSpace(value)
            ? null
            : value;
    }
}
