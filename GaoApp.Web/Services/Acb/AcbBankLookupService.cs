using System.Globalization;
using System.Text.Json;
using GaoApp.Application.Common.Helpers;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using static GaoApp.Web.Services.Acb.AcbProtocol;

namespace GaoApp.Web.Services.Acb;

// A bank query is a read, not a callback receipt or a POS payment confirmation.
public sealed class AcbBankLookupService(AppDbContext db, AcbProtocol protocol, TimeProvider? time = null)
{
    public const int PageSize = 50;
    private int StoreId => db.CurrentStoreId is > 0 ? db.CurrentStoreId.Value : throw new InvalidOperationException("Chưa xác định cửa hàng.");

    public async Task<AcbBankLookupPage> ReadAsync(DateTime? from, DateTime? to, int page, CancellationToken ct)
    {
        var clock = time ?? TimeProvider.System;
        var start = (from ?? clock.GetUtcNow().ToVietnamTime().DateTime).Date;
        var end = (to ?? start).Date;
        if (end < start || (end - start).TotalDays > 30)
            throw new ArgumentException("Chọn khoảng tra cứu tối đa 31 ngày, ngày kết thúc phải từ ngày bắt đầu trở đi.");
        if (page < 1) throw new ArgumentException("Số trang tra cứu phải từ 1 trở lên.");
        var storeId = StoreId;
        var settings = await db.Set<StoreAcbSettings>().AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == storeId, ct);
        if (settings?.Enabled != true) throw new InvalidOperationException("Chưa bật thanh toán ACB cho cửa hàng này. Hãy kiểm tra Cài đặt ACB.");
        if (string.IsNullOrWhiteSpace(settings.MerchantId) || settings.MerchantId.Length > 30)
            throw new InvalidOperationException("MerchantId của cửa hàng phải có từ 1 đến 30 ký tự. Hãy kiểm tra Cài đặt ACB.");
        if (!await db.StoreBankAccounts.AsNoTracking().AnyAsync(x => x.Id == settings.BankAccountId && x.StoreId == storeId && x.IsActive, ct))
            throw new InvalidOperationException("Tài khoản ACB của cửa hàng không hoạt động hoặc không còn hợp lệ.");
        var environment = ValidateEnvironment(settings.TokenEndpoint, settings.ApiBaseUrl, settings.QrEndpoint);
        var url = settings.ApiBaseUrl.TrimEnd('/') + "/acb/open/payments/qr-payment/v1/retrieve" +
            $"?fromDate={start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&toDate={end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" +
            $"&merchantId={Uri.EscapeDataString(settings.MerchantId)}&page={(page - 1).ToString(CultureInfo.InvariantCulture)}&size={PageSize}";
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        var response = await protocol.CallAsync(settings, HttpMethod.Get, url, null, deadline.Token);
        var queriedAtUtc = clock.GetUtcNow().UtcDateTime;
        var body = Path(response, "responseBody");
        var orders = Path(body, "orders");
        var pages = Path(body, "pagination", "totalPages");
        if (orders.ValueKind != JsonValueKind.Array || pages.ValueKind != JsonValueKind.Number ||
            !pages.TryGetInt32(out var totalPages) || totalPages < 0 || (totalPages == 0 && orders.GetArrayLength() > 0))
            throw InvalidResponse();
        if (page > Math.Max(1, totalPages)) throw new ArgumentException("Trang tra cứu không còn tồn tại. Bấm Tra cứu ACB để lấy lại từ trang đầu.");
        var totalValue = Path(body, "pagination", "totalRows");
        if (totalValue.ValueKind != JsonValueKind.Number || !totalValue.TryGetInt32(out var totalOrders) || totalOrders < 0 ||
            orders.GetArrayLength() > PageSize || totalOrders < orders.GetArrayLength()) throw InvalidResponse();
        var rows = new List<AcbBankLookupOrder>();
        var orderReferences = new HashSet<string>(StringComparer.Ordinal);
        foreach (var order in orders.EnumerateArray())
        {
            var id = ReadText(order, "orderId", required: true);
            var merchantId = ReadText(order, "merchantId");
            if (!orderReferences.Add(id) || (merchantId.Length > 0 && merchantId != settings.MerchantId)) throw InvalidResponse();
            var amount = ReadMoney(order, "amount");
            var details = Path(order, "transactionDetail");
            if (details.ValueKind is not (JsonValueKind.Array or JsonValueKind.Null or JsonValueKind.Undefined)) throw InvalidResponse();
            var transactions = new List<AcbBankLookupTransaction>();
            var numbers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var transaction in Items(details))
            {
                var number = ReadText(transaction, "transactionNumber", required: true);
                if (!numbers.Add(number)) throw InvalidResponse();
                transactions.Add(new(number, ReadMoney(transaction, "transactionAmount"), ReadText(transaction, "transactionStatus"),
                    ReadText(transaction, "transactionContent"), ReadText(transaction, "origPostDate")));
            }
            rows.Add(new(id, ReadText(order, "traceNumber"), amount, ReadText(order, "status"),
                ReadCreatedAt(order), transactions, null, null, 0, null));
        }
        if (rows.Count == 0)
            return new(start, end, page, PageSize, totalPages, totalOrders, queriedAtUtc, environment, settings.MerchantId, rows);
        var references = rows.Select(x => x.ProviderOrderId).Distinct().ToArray();
        var localOrders = await db.Set<AcbQrSession>().AsNoTracking()
            .Where(x => x.StoreId == storeId && references.Contains(x.ProviderOrderId))
            .Join(db.Orders.AsNoTracking().Where(x => x.StoreId == storeId), session => session.OrderId, order => order.Id,
                (session, order) => new { session.ProviderOrderId, order.Id, order.OrderNumber }).ToListAsync(ct);
        var callbacks = await db.Set<AcbQrNotificationItem>().AsNoTracking()
            .Where(x => x.StoreId == storeId && x.Receipt.StoreId == storeId && references.Contains(x.ProviderOrderId))
            .GroupBy(x => x.ProviderOrderId)
            .Select(g => new { ProviderOrderId = g.Key, Count = g.Select(x => x.ReceiptId).Distinct().Count(), LastReceived = g.Max(x => x.Receipt.CreatedAtUtc) })
            .ToListAsync(ct);
        rows = rows.Select(row =>
        {
            var localOrder = localOrders.SingleOrDefault(x => x.ProviderOrderId == row.ProviderOrderId);
            var callback = callbacks.SingleOrDefault(x => x.ProviderOrderId == row.ProviderOrderId);
            return row with { OrderId = localOrder?.Id, OrderNumber = localOrder?.OrderNumber,
                CallbackCount = callback?.Count ?? 0, LastCallbackAtUtc = callback?.LastReceived };
        }).ToList();
        return new(start, end, page, PageSize, totalPages, totalOrders, queriedAtUtc, environment, settings.MerchantId, rows);
    }

    private static decimal ReadMoney(JsonElement value, string name)
    {
        var field = Path(value, name);
        if (field.ValueKind is JsonValueKind.String or JsonValueKind.Number &&
            decimal.TryParse(field.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)) return amount;
        throw InvalidResponse();
    }

    private static string ReadText(JsonElement value, string name, bool required = false)
    {
        var field = Path(value, name);
        if (field.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined && !required) return "";
        if (field.ValueKind is not (JsonValueKind.String or JsonValueKind.Number)) throw InvalidResponse();
        var text = field.ToString();
        if (required && string.IsNullOrWhiteSpace(text)) throw InvalidResponse();
        return text;
    }

    private static DateTime? ReadCreatedAt(JsonElement order)
    {
        var value = ReadText(order, "createdDatetime");
        if (value.Length == 0) return null;
        if (value.Length >= 5 && (value[^5] == '+' || value[^5] == '-') && value[^4..].All(char.IsAsciiDigit))
            value = value.Insert(value.Length - 2, ":");
        var hasOffset = value.EndsWith('Z') || (value.Length >= 6 && (value[^6] == '+' || value[^6] == '-') && value[^3] == ':');
        if (!hasOffset || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant)) throw InvalidResponse();
        return instant.UtcDateTime;
    }

    private static AcbApiException InvalidResponse() => new("ACB trả dữ liệu giao dịch hoặc phân trang không hợp lệ. Hãy thử tra cứu lại; kết quả này chưa được dùng để đối soát.");
}

public sealed record AcbBankLookupTransaction(string TransactionNumber, decimal Amount, string Status, string Content, string PostedDate);
public sealed record AcbBankLookupOrder(string ProviderOrderId, string TraceNumber, decimal RequestedAmount, string Status, DateTime? CreatedAtUtc,
    IReadOnlyList<AcbBankLookupTransaction> Transactions, int? OrderId, string? OrderNumber, int CallbackCount, DateTime? LastCallbackAtUtc);
public sealed record AcbBankLookupPage(DateTime From, DateTime To, int Page, int PageSize, int TotalPages, int? TotalOrders,
    DateTime QueriedAtUtc, string Environment, string MerchantId, IReadOnlyList<AcbBankLookupOrder> Orders)
{
    public int TransactionCount => Orders.Sum(x => x.Transactions.Count);
    public decimal PaidAmount => Orders.SelectMany(x => x.Transactions).Where(x => IsPaid(x.Status)).Sum(x => x.Amount);
}
