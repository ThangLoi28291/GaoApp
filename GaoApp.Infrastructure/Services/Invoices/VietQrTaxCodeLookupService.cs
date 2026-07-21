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
            ? 8
            : _options.TimeoutSeconds;

        var candidates = BuildLookupCandidates(originalTaxCode);

        string? lastErrorMessage = null;

        foreach (var lookupTaxCode in candidates)
        {
            var url = $"{baseUrl}/v2/business/{Uri.EscapeDataString(lookupTaxCode)}";

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                using var request = new HttpRequestMessage(HttpMethod.Get, url);

                request.Headers.Accept.Clear();
                request.Headers.Accept.ParseAdd("application/json");

                using var response = await _httpClient.SendAsync(
                    request,
                    timeoutCts.Token);

                var raw = await response.Content.ReadAsStringAsync(timeoutCts.Token);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    lastErrorMessage = "API tra cứu MST đang bị giới hạn lượt gọi. Vui lòng thử lại sau.";
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    lastErrorMessage = $"Tra cứu MST thất bại. HTTP {(int)response.StatusCode}.";
                    continue;
                }

                var parsed = ParseVietQrResponse(
                    originalTaxCode,
                    raw);

                if (parsed != null)
                {
                    return Result<TaxCodeLookupResultDto>.Success(parsed);
                }

                lastErrorMessage = "Không tìm thấy thông tin doanh nghiệp từ VietQR.";
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                lastErrorMessage = "Tra cứu VietQR quá thời gian chờ.";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "VietQR tax code lookup failed. TaxCode={TaxCode}, LookupTaxCode={LookupTaxCode}",
                    originalTaxCode,
                    lookupTaxCode);

                lastErrorMessage = "Không gọi được VietQR.";
            }
        }

        return Result<TaxCodeLookupResultDto>.Failure(
            Error.NotFound(
                string.IsNullOrWhiteSpace(lastErrorMessage)
                    ? "Không tìm thấy thông tin doanh nghiệp từ API tra MST."
                    : lastErrorMessage));
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
        string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(raw);

            var root = doc.RootElement;

            var code =
                FindString(root, "code") ??
                FindString(root, "errorCode");

            var desc =
                FindString(root, "desc") ??
                FindString(root, "message") ??
                FindString(root, "description");

            // VietQR thường trả code "00" khi thành công.
            if (!string.IsNullOrWhiteSpace(code) &&
                !code.Equals("00", StringComparison.OrdinalIgnoreCase) &&
                !code.Equals("200", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            JsonElement data = root;

            if (TryFindProperty(root, "data", out var dataElement) &&
                dataElement.ValueKind == JsonValueKind.Object)
            {
                data = dataElement;
            }

            var name =
                FindString(data, "name") ??
                FindString(data, "companyName") ??
                FindString(data, "businessName") ??
                FindString(data, "legalName");

            var internationalName =
                FindString(data, "internationalName") ??
                FindString(data, "international_name");

            var shortName =
                FindString(data, "shortName") ??
                FindString(data, "short_name");

            var address =
                FindString(data, "address") ??
                FindString(data, "companyAddress") ??
                FindString(data, "businessAddress");

            var representative =
                FindString(data, "representativeName") ??
                FindString(data, "legalRepresentative") ??
                FindString(data, "ownerName");

            var returnedTaxCode =
                FindString(data, "id") ??
                FindString(data, "taxCode") ??
                FindString(data, "mst");

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
            return null;
        }
    }

    private static bool TryFindProperty(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals(propertyName) ||
                    property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }

                if (TryFindProperty(property.Value, propertyName, out value))
                    return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
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

    private static string? FindString(
        JsonElement element,
        string propertyName)
    {
        if (!TryFindProperty(element, propertyName, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
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