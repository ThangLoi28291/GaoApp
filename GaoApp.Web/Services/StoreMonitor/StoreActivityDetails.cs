using System.Collections;
using System.Globalization;

namespace GaoApp.Web.Services.StoreMonitor;

/// <summary>Allowlisted summaries of a successful response; never serialize a request, customer or note.</summary>
public static class StoreActivityDetails
{
    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");
    public sealed record Description(string? Document, string? Detail, string? WorkKey = null, string? Text = null);

    public static Description Describe(string controller, string action, IDictionary<string, object?> arguments, object? payload)
    {
        var result = Property(payload, "draft") ?? Property(payload, "cart") ?? payload;
        arguments.TryGetValue("request", out var request);
        arguments.TryGetValue("dto", out var dto);
        request ??= dto;
        int? parent = Positive(Property(result, "orderId")) ?? Positive(Property(result, "documentId")) ??
            Positive(Property(result, "stockDocumentId")) ?? Positive(Property(result, "stockCountDocumentId"));
        if (controller == "POS" && action is "HoldOrder" or "HoldCurrentCart")
            parent = Positive(Property(result, "heldOrderId")) ?? parent;
        foreach (var name in new[] { "orderId", "documentId", "stockDocumentId", "stockCountDocumentId" })
        {
            if (parent is null && arguments.TryGetValue(name, out var value)) parent = Positive(value);
            parent ??= Positive(Property(request, name));
        }
        if (parent is null && controller is "StockDocuments" or "StockDocumentManagement" or "StockCounts" or "StockTransfers")
        {
            if (arguments.TryGetValue("id", out var id)) parent = Positive(id);
        }
        if (parent is null && action is "CreateReceipt" or "Create" && controller != "POS") parent = Positive(Property(result, "id"));
        string? document = parent is { } found ? $"#{found}" : null;
        var kind = controller switch
        {
            "POS" => "order", "StockCounts" => "count", "StockTransfers" => "transfer",
            "StockDocuments" or "StockDocumentManagement" or "WarehouseReceiving" or "ReceiptIntake" => "receipt",
            _ => null
        };
        string? workKey = parent is { } workId && kind is not null ? $"{kind}:{workId}" : null;
        if (document is null && controller == "StockCounts" && arguments.TryGetValue("lineId", out var countLine) && Positive(countLine) is { } countLineId)
            document = $"Dòng kiểm kê #{countLineId}";
        if (controller == "LabelPrinting")
        {
            var id = Positive(Property(result, "id"));
            if (id is null && arguments.TryGetValue("id", out var routeId)) id = Positive(routeId);
            if (id is { } labelId) document = action is "Print" or "Confirm" or "Cancel" ? $"Lệnh in #{labelId}" : $"Phiếu tem #{labelId}";
            if (action is "Print" && Positive(Property(request, "taskId")) is { } taskId)
                workKey = $"label:{taskId}";
            else if (id is { } identity)
                workKey = $"{(action is "Print" or "Confirm" or "Cancel" ? "label-job" : "label")}:{identity}";
        }

        var parts = new List<string>();
        var quantity = arguments.TryGetValue("qty", out var qty) ? Number(qty) : null;
        quantity ??= Number(Property(request, "quantity")) ?? Number(Property(request, "qty"));
        if (quantity is > 0) parts.Add($"Số lượng {Format(quantity.Value)}");
        if (controller == "StockCounts" && Number(Property(request, "countedQty")) is { } counted)
            parts.Add($"Đã đếm {Format(counted)}");
        if (Property(result, "lines") is IEnumerable lines)
        {
            var items = lines.Cast<object>().Take(500).ToArray();
            if (controller == "POS" && arguments.TryGetValue("variantId", out var variantId))
            {
                var item = items.FirstOrDefault(x => Positive(Property(x, "variantId")) == Positive(variantId));
                if (Property(item, "itemName") is string { Length: > 0 } name) parts.Insert(0, name);
            }
            parts.Add($"{items.Length} mặt hàng");
        }
        else if (controller == "LabelPrinting" && Property(request, "lines") is IEnumerable labels)
        {
            var items = labels.Cast<object>().Take(500).ToArray();
            var copies = items.Sum(x => Number(Property(x, "copies")) ?? Number(Property(x, "quantity")) ?? 0);
            parts.Add($"{items.Length} mặt hàng" + (copies > 0 ? $" · {Format(copies)} tem" : ""));
        }
        if (controller == "POS" && Number(Property(result, "grandTotal")) is { } total)
            parts.Add($"Tổng {Format(total)} ₫");
        var received = Number(Property(request, "amount"));
        if (controller == "POS" && received is > 0)
            parts.Add($"Ghi nhận {Format(received.Value)} ₫");
        return new(document, parts.Count > 0 ? string.Join(" · ", parts) : null, workKey);
    }
    public static object? Property(object? value, string name) => value?.GetType().GetProperties()
        .FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && x.GetIndexParameters().Length == 0)?.GetValue(value);
    private static int? Positive(object? value) => value is int id && id > 0 ? id : null;
    private static decimal? Number(object? value) => value switch { decimal d => d, int i => i, long l => l, _ => null };
    private static string Format(decimal value) => value.ToString("#,0.###", Vietnamese);
}
