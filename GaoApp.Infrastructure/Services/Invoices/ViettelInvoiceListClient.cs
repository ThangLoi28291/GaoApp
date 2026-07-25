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
                            $"Đồng bộ danh sách Viettel thất bại. HTTP {(int)response.StatusCode}."));
                }

                var parsed = ParseGetInvoicesResponse(raw);

                if (!parsed.IsSuccess)
                {
                    sw.Stop();

                    return Result<ViettelInvoiceListSyncResultDto>.Failure(
                        Error.Validation(
                            parsed.ErrorCode ?? "Viettel.GetInvoicesBusinessFailed",
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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            sw.Stop();

            return Result<ViettelInvoiceListSyncResultDto>.Failure(
                Error.Validation(
                    "Viettel.GetInvoicesTimeout",
                    "Đồng bộ danh sách Viettel timeout."));
        }
        catch (Exception)
        {
            sw.Stop();

            return Result<ViettelInvoiceListSyncResultDto>.Failure(
                Error.Validation(
                    "Viettel.GetInvoicesException",
                    "Không gọi được dịch vụ đồng bộ danh sách Viettel."));
        }
    }

    private static GetInvoicesParseResult ParseGetInvoicesResponse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return InvalidResponse(
                "Viettel.GetInvoicesInvalidResponse",
                "Viettel không trả dữ liệu danh sách hóa đơn.");
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
                return InvalidResponse(
                    "Viettel.GetInvoicesInvalidResponse",
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
                return InvalidResponse(
                    "Viettel.GetInvoicesInvalidResponse",
                    "Viettel trả về envelope danh sách hóa đơn không hợp lệ.");
            }

            if (containers.Any(
                    container =>
                        ViettelClientHelper.HasDirectFailureMarker(
                            container.Element,
                            ViettelClientHelper.IsExplicitViettelSuccessCode)))
            {
                return InvalidResponse(
                    "Viettel.GetInvoicesBusinessFailed",
                    "Viettel trả lỗi khi lấy danh sách hóa đơn.");
            }

            var candidates = containers
                .Where(
                    container =>
                        HasDirectListShape(container.Element))
                .ToList();

            if (candidates.Count > 1)
            {
                return InvalidResponse(
                    "Viettel.GetInvoicesConflictingResponse",
                    "Viettel trả về phản hồi mâu thuẫn hoặc không đủ điều kiện xác nhận.");
            }

            if (candidates.Count == 0)
            {
                return InvalidResponse(
                    "Viettel.GetInvoicesAmbiguousResponse",
                    "Viettel trả về phản hồi danh sách hóa đơn không nhận diện được.");
            }

            var responseContainer = candidates[0].Element;
            var items = new List<ViettelInvoiceListItemDto>();
            var hasInvoices =
                ViettelClientHelper.GetUniqueDirectProperty(
                    responseContainer,
                    "invoices",
                    out var invoicesElement) ==
                DirectPropertyLookupResult.Found;

            if (hasInvoices &&
                invoicesElement.ValueKind != JsonValueKind.Array)
            {
                return InvalidResponse(
                    "Viettel.GetInvoicesInvalidResponse",
                    "Viettel trả về trường invoices không hợp lệ.");
            }

            if (hasInvoices)
            {
                foreach (var item in invoicesElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        return InvalidResponse(
                            "Viettel.GetInvoicesInvalidResponse",
                            "Viettel trả về phần tử hóa đơn không hợp lệ.");
                    }

                    var parsedItem = new ViettelInvoiceListItemDto
                    {
                        InvoiceId = ViettelClientHelper.GetDirectString(item, "invoiceId"),
                        InvoiceType = ViettelClientHelper.GetDirectString(item, "invoiceType"),
                        TemplateCode = ViettelClientHelper.GetDirectString(item, "templateCode"),
                        InvoiceSeri = ViettelClientHelper.GetDirectString(item, "invoiceSeri"),
                        InvoiceNumber = ViettelClientHelper.GetDirectString(item, "invoiceNumber"),
                        InvoiceNo = ViettelClientHelper.GetDirectString(item, "invoiceNo"),
                        Currency = ViettelClientHelper.GetDirectString(item, "currency"),
                        Total = FindDecimalProperty(item, "total"),
                        TotalBeforeTax = FindDecimalProperty(item, "totalBeforeTax"),
                        TaxAmount = FindDecimalProperty(item, "taxAmount"),
                        IssueDate = FindLongProperty(item, "issueDate"),
                        IssueDateStr = ViettelClientHelper.GetDirectString(item, "issueDateStr"),
                        State = FindIntProperty(item, "state"),
                        StateCode = FindIntProperty(item, "stateCode"),
                        PaymentStatus = FindIntProperty(item, "paymentStatus"),
                        PaymentStatusName = ViettelClientHelper.GetDirectString(item, "paymentStatusName"),
                        BuyerName = ViettelClientHelper.GetDirectString(item, "buyerName"),
                        BuyerTaxCode = ViettelClientHelper.GetDirectString(item, "buyerTaxCode"),
                        SupplierTaxCode = ViettelClientHelper.GetDirectString(item, "supplierTaxCode"),
                        TransactionUuid = ViettelClientHelper.GetDirectString(item, "transactionUuid"),
                        OriginalInvoiceId = ViettelClientHelper.GetDirectString(item, "originalInvoiceId")
                    };

                    if (string.IsNullOrWhiteSpace(parsedItem.InvoiceNo) &&
                        string.IsNullOrWhiteSpace(parsedItem.InvoiceId) &&
                        string.IsNullOrWhiteSpace(parsedItem.TransactionUuid))
                    {
                        return InvalidResponse(
                            "Viettel.GetInvoicesInvalidResponse",
                            "Viettel trả về hóa đơn thiếu định danh ổn định.");
                    }

                    items.Add(parsedItem);
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
            var totalRows = items.Count;

            if (hasTotal &&
                !TryReadNonNegativeInt(totalRowsElement, out totalRows))
            {
                return InvalidResponse(
                    "Viettel.GetInvoicesInvalidResponse",
                    "Viettel trả về tổng số hóa đơn không hợp lệ.");
            }

            if (!hasInvoices && !hasTotal)
            {
                return InvalidResponse(
                    "Viettel.GetInvoicesAmbiguousResponse",
                    "Viettel trả về phản hồi danh sách hóa đơn không nhận diện được.");
            }

            if (!hasInvoices && totalRows > 0)
            {
                return InvalidResponse(
                    "Viettel.GetInvoicesAmbiguousResponse",
                    "Viettel trả về tổng số hóa đơn nhưng thiếu danh sách hóa đơn.");
            }

            if (hasInvoices && hasTotal && totalRows < items.Count)
            {
                return InvalidResponse(
                    "Viettel.GetInvoicesInvalidResponse",
                    "Viettel trả về tổng số hóa đơn nhỏ hơn số phần tử trong danh sách.");
            }

            return new GetInvoicesParseResult
            {
                IsSuccess = true,
                TotalRows = totalRows,
                Items = items
            };
        }
        catch
        {
            return InvalidResponse(
                "Viettel.GetInvoicesInvalidResponse",
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

    private static GetInvoicesParseResult InvalidResponse(
        string errorCode,
        string errorMessage) =>
        new()
        {
            IsSuccess = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };

    private static bool TryReadNonNegativeInt(
        JsonElement element,
        out int value)
    {
        if (element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt32(out value))
        {
            return value >= 0;
        }

        if (element.ValueKind == JsonValueKind.String &&
            int.TryParse(
                element.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value))
        {
            return value >= 0;
        }

        value = 0;
        return false;
    }

    private static int? FindIntProperty(
        JsonElement element,
        string propertyName)
    {
        var value = ViettelClientHelper.GetDirectString(element, propertyName);

        if (int.TryParse(value, out var result))
            return result;

        return null;
    }

    private static long? FindLongProperty(
        JsonElement element,
        string propertyName)
    {
        var value = ViettelClientHelper.GetDirectString(element, propertyName);

        if (long.TryParse(value, out var result))
            return result;

        return null;
    }

    private static decimal FindDecimalProperty(
        JsonElement element,
        string propertyName)
    {
        var value = ViettelClientHelper.GetDirectString(element, propertyName);

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
