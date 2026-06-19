using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Domain.Enums;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GaoApp.Infrastructure.Services.Invoices;

public class ViettelInvoiceListClient : IViettelInvoiceListClient
{
    private readonly HttpClient _httpClient;

    public ViettelInvoiceListClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<Result<ViettelInvoiceListSyncResultDto>> GetInvoicesAsync(
        string baseUrl,
        string username,
        string password,
        InvoiceProviderAuthMode authMode,
        string supplierTaxCode,
        string invoiceType,
        string templateCode,
        string invoiceSeries,
        DateTime fromDate,
        DateTime toDate,
        int pageSize = 100,
        CancellationToken ct = default)
    {
        if (authMode != InvoiceProviderAuthMode.BasicAuth)
        {
            return Result<ViettelInvoiceListSyncResultDto>.Failure(
                Error.Validation("Viettel.AuthModeUnsupported", "Đồng bộ danh sách Viettel đang dùng Basic Auth."));
        }

        if (string.IsNullOrWhiteSpace(baseUrl) ||
            string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password) ||
            string.IsNullOrWhiteSpace(supplierTaxCode))
        {
            return Result<ViettelInvoiceListSyncResultDto>.Failure(
                Error.Validation("Viettel.RequiredMissing", "Thiếu cấu hình Viettel để đồng bộ danh sách hóa đơn."));
        }

        if (toDate.Date < fromDate.Date)
        {
            return Result<ViettelInvoiceListSyncResultDto>.Failure(
                Error.Validation("Viettel.DateInvalid", "Đến ngày phải lớn hơn hoặc bằng từ ngày."));
        }

        if ((toDate.Date - fromDate.Date).TotalDays > 92)
        {
            return Result<ViettelInvoiceListSyncResultDto>.Failure(
                Error.Validation("Viettel.DateRangeTooLong", "Viettel chỉ cho đồng bộ tối đa khoảng 3 tháng/lần."));
        }

        pageSize = pageSize <= 0 ? 100 : pageSize;
        pageSize = Math.Min(pageSize, 500);

        var url =
            $"{baseUrl.TrimEnd('/')}/InvoiceAPI/InvoiceUtilsWS/getInvoices/{Uri.EscapeDataString(supplierTaxCode.Trim())}";

        var sw = Stopwatch.StartNew();

        var allInvoices = new List<ViettelInvoiceListItemDto>();
        var totalRows = 0;
        var pageNum = 1;
        const int maxPage = 100;

        try
        {
            while (pageNum <= maxPage)
            {
                var body = new
                {
                    supplierTaxCode = supplierTaxCode.Trim(),
                    startDate = fromDate.ToString("yyyy-MM-dd"),
                    endDate = toDate.ToString("yyyy-MM-dd"),
                    invoiceType = string.IsNullOrWhiteSpace(invoiceType) ? "1" : invoiceType.Trim(),
                    templateCode = string.IsNullOrWhiteSpace(templateCode) ? null : templateCode.Trim(),
                    invoiceSeri = string.IsNullOrWhiteSpace(invoiceSeries) ? null : invoiceSeries.Trim(),
                    rowPerPage = pageSize,
                    pageNum,
                    getAll = true
                };

                var requestBodyJson = JsonSerializer.Serialize(
                    body,
                    new JsonSerializerOptions
                    {
                        WriteIndented = false,
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    });

                using var request = new HttpRequestMessage(HttpMethod.Post, url);

                request.Headers.Accept.Clear();
                request.Headers.Accept.ParseAdd("application/json");
                request.Headers.Authorization = BuildBasicAuthHeader(username, password);

                request.Content = new StringContent(
                    requestBodyJson,
                    Encoding.UTF8,
                    "application/json");

                using var response = await _httpClient.SendAsync(request, ct);
                var raw = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    sw.Stop();

                    return Result<ViettelInvoiceListSyncResultDto>.Failure(
                        Error.Validation(
                            "Viettel.GetInvoicesFailed",
                            $"Đồng bộ danh sách Viettel thất bại. HTTP {(int)response.StatusCode}. Response: {Trim(raw, 1000)}"));
                }

                var parsed = ParseGetInvoicesResponse(raw);

                if (!parsed.IsSuccess)
                {
                    sw.Stop();

                    return Result<ViettelInvoiceListSyncResultDto>.Failure(
                        Error.Validation(
                            "Viettel.GetInvoicesBusinessFailed",
                            parsed.ErrorMessage ?? "Viettel trả lỗi khi lấy danh sách hóa đơn."));
                }

                totalRows = parsed.TotalRows;
                allInvoices.AddRange(parsed.Items);

                if (allInvoices.Count >= totalRows || parsed.Items.Count == 0)
                    break;

                pageNum++;
            }

            sw.Stop();

            return Result<ViettelInvoiceListSyncResultDto>.Success(
                new ViettelInvoiceListSyncResultDto
                {
                    FromDate = fromDate.Date,
                    ToDate = toDate.Date,
                    TotalRemoteRows = totalRows,
                    RemoteItemsLoaded = allInvoices.Count,
                    RemoteInvoices = allInvoices,
                    DurationMs = sw.ElapsedMilliseconds
                });
        }
        catch (TaskCanceledException)
        {
            sw.Stop();

            return Result<ViettelInvoiceListSyncResultDto>.Failure(
                Error.Validation("Viettel.GetInvoicesTimeout", "Đồng bộ danh sách Viettel timeout."));
        }
        catch (Exception ex)
        {
            sw.Stop();

            return Result<ViettelInvoiceListSyncResultDto>.Failure(
                Error.Validation("Viettel.GetInvoicesException", $"Lỗi đồng bộ danh sách Viettel: {ex.Message}"));
        }
    }

    private static GetInvoicesParseResult ParseGetInvoicesResponse(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            var errorCode = FindStringProperty(root, "errorCode");
            var description = FindStringProperty(root, "description");

            if (!string.IsNullOrWhiteSpace(errorCode) &&
                !string.Equals(errorCode, "null", StringComparison.OrdinalIgnoreCase))
            {
                return new GetInvoicesParseResult
                {
                    IsSuccess = false,
                    ErrorCode = errorCode,
                    ErrorMessage = description ?? errorCode
                };
            }

            var totalRows = FindIntProperty(root, "totalRows") ?? 0;

            var items = new List<ViettelInvoiceListItemDto>();

            if (TryFindProperty(root, "invoices", out var invoicesElement) &&
                invoicesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in invoicesElement.EnumerateArray())
                {
                    items.Add(new ViettelInvoiceListItemDto
                    {
                        InvoiceId = FindStringProperty(item, "invoiceId"),
                        InvoiceType = FindStringProperty(item, "invoiceType"),
                        TemplateCode = FindStringProperty(item, "templateCode"),
                        InvoiceSeri = FindStringProperty(item, "invoiceSeri"),
                        InvoiceNumber = FindStringProperty(item, "invoiceNumber"),
                        InvoiceNo = FindStringProperty(item, "invoiceNo"),
                        Currency = FindStringProperty(item, "currency"),
                        Total = FindDecimalProperty(item, "total"),
                        TotalBeforeTax = FindDecimalProperty(item, "totalBeforeTax"),
                        TaxAmount = FindDecimalProperty(item, "taxAmount"),
                        IssueDate = FindLongProperty(item, "issueDate"),
                        IssueDateStr = FindStringProperty(item, "issueDateStr"),
                        State = FindIntProperty(item, "state"),
                        StateCode = FindIntProperty(item, "stateCode"),
                        PaymentStatus = FindIntProperty(item, "paymentStatus"),
                        PaymentStatusName = FindStringProperty(item, "paymentStatusName"),
                        BuyerName = FindStringProperty(item, "buyerName"),
                        BuyerTaxCode = FindStringProperty(item, "buyerTaxCode"),
                        SupplierTaxCode = FindStringProperty(item, "supplierTaxCode"),
                        TransactionUuid = FindStringProperty(item, "transactionUuid"),
                        OriginalInvoiceId = FindStringProperty(item, "originalInvoiceId")
                    });
                }
            }

            return new GetInvoicesParseResult
            {
                IsSuccess = true,
                TotalRows = totalRows,
                Items = items
            };
        }
        catch (Exception ex)
        {
            return new GetInvoicesParseResult
            {
                IsSuccess = false,
                ErrorCode = "PARSE_ERROR",
                ErrorMessage = $"Không đọc được response getInvoices: {ex.Message}"
            };
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

    private static bool TryFindProperty(JsonElement element, string propertyName, out JsonElement value)
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

        return null;
    }

    private static int? FindIntProperty(JsonElement element, string propertyName)
    {
        var value = FindStringProperty(element, propertyName);

        if (int.TryParse(value, out var result))
            return result;

        return null;
    }

    private static long? FindLongProperty(JsonElement element, string propertyName)
    {
        var value = FindStringProperty(element, propertyName);

        if (long.TryParse(value, out var result))
            return result;

        return null;
    }

    private static decimal FindDecimalProperty(JsonElement element, string propertyName)
    {
        var value = FindStringProperty(element, propertyName);

        if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
            return result;

        return 0m;
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

    private class GetInvoicesParseResult
    {
        public bool IsSuccess { get; set; }

        public int TotalRows { get; set; }

        public List<ViettelInvoiceListItemDto> Items { get; set; } = new();

        public string? ErrorCode { get; set; }

        public string? ErrorMessage { get; set; }
    }
}