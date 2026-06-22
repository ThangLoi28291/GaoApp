using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Domain.Enums;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

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
                Error.Validation(
                    "Viettel.AuthModeUnsupported",
                    "Đồng bộ danh sách Viettel đang dùng Basic Auth."));
        }

        if (string.IsNullOrWhiteSpace(baseUrl) ||
            string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password) ||
            string.IsNullOrWhiteSpace(supplierTaxCode))
        {
            return Result<ViettelInvoiceListSyncResultDto>.Failure(
                Error.Validation(
                    "Viettel.RequiredMissing",
                    "Thiếu cấu hình Viettel để đồng bộ danh sách hóa đơn."));
        }

        if (toDate.Date < fromDate.Date)
        {
            return Result<ViettelInvoiceListSyncResultDto>.Failure(
                Error.Validation(
                    "Viettel.DateInvalid",
                    "Đến ngày phải lớn hơn hoặc bằng từ ngày."));
        }

        if ((toDate.Date - fromDate.Date).TotalDays > 92)
        {
            return Result<ViettelInvoiceListSyncResultDto>.Failure(
                Error.Validation(
                    "Viettel.DateRangeTooLong",
                    "Viettel chỉ cho đồng bộ tối đa khoảng 3 tháng/lần."));
        }

        pageSize = pageSize <= 0 ? 100 : pageSize;
        pageSize = Math.Min(pageSize, 500);

        var normalizedBaseUrl = ViettelClientHelper.NormalizeBaseUrl(baseUrl);

        var url =
            $"{normalizedBaseUrl}/InvoiceAPI/InvoiceUtilsWS/getInvoices/{Uri.EscapeDataString(supplierTaxCode.Trim())}";

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
                    invoiceType = string.IsNullOrWhiteSpace(invoiceType)
                        ? "1"
                        : invoiceType.Trim(),
                    templateCode = string.IsNullOrWhiteSpace(templateCode)
                        ? null
                        : templateCode.Trim(),
                    invoiceSeri = string.IsNullOrWhiteSpace(invoiceSeries)
                        ? null
                        : invoiceSeries.Trim(),
                    rowPerPage = pageSize,
                    pageNum,
                    getAll = true
                };

                var requestBodyJson = ViettelClientHelper.SerializeJson(body);

                using var request = new HttpRequestMessage(HttpMethod.Post, url);

                request.Headers.Accept.Clear();
                request.Headers.Accept.ParseAdd("application/json");

                request.Headers.Authorization = ViettelClientHelper.BuildBasicAuthHeader(
                    username,
                    password);

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
                            $"Đồng bộ danh sách Viettel thất bại. HTTP {(int)response.StatusCode}. Response: {ViettelClientHelper.Trim(raw, 1000)}"));
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
                Error.Validation(
                    "Viettel.GetInvoicesTimeout",
                    "Đồng bộ danh sách Viettel timeout."));
        }
        catch (Exception ex)
        {
            sw.Stop();

            return Result<ViettelInvoiceListSyncResultDto>.Failure(
                Error.Validation(
                    "Viettel.GetInvoicesException",
                    $"Lỗi đồng bộ danh sách Viettel: {ex.Message}"));
        }
    }

    private static GetInvoicesParseResult ParseGetInvoicesResponse(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            var errorCode =
                ViettelClientHelper.FindStringProperty(root, "errorCode") ??
                ViettelClientHelper.FindStringProperty(root, "code");

            var description =
                ViettelClientHelper.FindStringProperty(root, "description") ??
                ViettelClientHelper.FindStringProperty(root, "message");

            if (!ViettelClientHelper.IsViettelSuccessCode(errorCode))
            {
                return new GetInvoicesParseResult
                {
                    IsSuccess = false,
                    ErrorCode = errorCode,
                    ErrorMessage = description ?? errorCode
                };
            }

            var totalRows = FindIntProperty(root, "totalRows") ??
                            FindIntProperty(root, "totalRow") ??
                            0;

            var items = new List<ViettelInvoiceListItemDto>();

            if (ViettelClientHelper.TryFindProperty(root, "invoices", out var invoicesElement) &&
                invoicesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in invoicesElement.EnumerateArray())
                {
                    items.Add(new ViettelInvoiceListItemDto
                    {
                        InvoiceId = ViettelClientHelper.FindStringProperty(item, "invoiceId"),
                        InvoiceType = ViettelClientHelper.FindStringProperty(item, "invoiceType"),
                        TemplateCode = ViettelClientHelper.FindStringProperty(item, "templateCode"),
                        InvoiceSeri = ViettelClientHelper.FindStringProperty(item, "invoiceSeri"),
                        InvoiceNumber = ViettelClientHelper.FindStringProperty(item, "invoiceNumber"),
                        InvoiceNo = ViettelClientHelper.FindStringProperty(item, "invoiceNo"),
                        Currency = ViettelClientHelper.FindStringProperty(item, "currency"),
                        Total = FindDecimalProperty(item, "total"),
                        TotalBeforeTax = FindDecimalProperty(item, "totalBeforeTax"),
                        TaxAmount = FindDecimalProperty(item, "taxAmount"),
                        IssueDate = FindLongProperty(item, "issueDate"),
                        IssueDateStr = ViettelClientHelper.FindStringProperty(item, "issueDateStr"),
                        State = FindIntProperty(item, "state"),
                        StateCode = FindIntProperty(item, "stateCode"),
                        PaymentStatus = FindIntProperty(item, "paymentStatus"),
                        PaymentStatusName = ViettelClientHelper.FindStringProperty(item, "paymentStatusName"),
                        BuyerName = ViettelClientHelper.FindStringProperty(item, "buyerName"),
                        BuyerTaxCode = ViettelClientHelper.FindStringProperty(item, "buyerTaxCode"),
                        SupplierTaxCode = ViettelClientHelper.FindStringProperty(item, "supplierTaxCode"),
                        TransactionUuid = ViettelClientHelper.FindStringProperty(item, "transactionUuid"),
                        OriginalInvoiceId = ViettelClientHelper.FindStringProperty(item, "originalInvoiceId")
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

    private static int? FindIntProperty(
        JsonElement element,
        string propertyName)
    {
        var value = ViettelClientHelper.FindStringProperty(element, propertyName);

        if (int.TryParse(value, out var result))
            return result;

        return null;
    }

    private static long? FindLongProperty(
        JsonElement element,
        string propertyName)
    {
        var value = ViettelClientHelper.FindStringProperty(element, propertyName);

        if (long.TryParse(value, out var result))
            return result;

        return null;
    }

    private static decimal FindDecimalProperty(
        JsonElement element,
        string propertyName)
    {
        var value = ViettelClientHelper.FindStringProperty(element, propertyName);

        if (decimal.TryParse(
                value,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var result))
        {
            return result;
        }

        return 0m;
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