using System.Globalization;
using System.Text.Json;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Purchases;

/// <summary>
/// Rebuilds only the latest correction round from append-only receipt evidence.
/// A resubmission closes the round so later commercial review does not move rows.
/// </summary>
public static class PurchaseReceiptRevisionReviewPolicy
{
    private static readonly HashSet<string> ReviewFields = new(
        PurchaseReceiptAuditEvidence.LineFields.Where(field => field is not
            nameof(StockDocumentLine.IsDeleted) and not
            nameof(StockDocumentLine.BaseQuantity) and not
            nameof(StockDocumentLine.VatAmount) and not
            nameof(StockDocumentLine.UnitPriceAfterVat) and not
            nameof(StockDocumentLine.UnitCost) and not
            nameof(StockDocumentLine.LineTotal)), StringComparer.Ordinal);

    public static void Apply(
        StockDocumentDto document,
        int storeId,
        IReadOnlyList<PurchaseReceiptAuditEvent>? auditEvents)
    {
        document.RevisionReview = null;
        foreach (var line in document.Lines)
        {
            line.RevisionChangeKind = null;
            line.RevisionChangedFields = [];
            line.RevisionChangeSummary = string.Empty;
            line.PreviousRevisionQuantity = null;
        }

        if (auditEvents is null || storeId <= 0 ||
            document.Type != StockDocumentType.Receipt ||
            document.Status is not (StockDocumentStatus.PendingApproval or StockDocumentStatus.Rejected))
            return;

        var events = auditEvents.Where(item => item.IsSuccess &&
                item.StoreId == storeId && item.StockDocumentId == document.Id)
            .OrderBy(item => item.OccurredAtUtc).ThenBy(item => item.Id).ToArray();
        var returnedIndex = Array.FindLastIndex(events, item => item.EventType is
            PurchaseReceiptAuditEventType.ReceiptRejected or
            PurchaseReceiptAuditEventType.RevisionReturnedForEditing);
        if (returnedIndex < 0) return;

        var returned = events[returnedIndex];
        var afterReturn = events.Skip(returnedIndex + 1).ToArray();
        var submittedIndex = Array.FindIndex(afterReturn, item => item.EventType is
            PurchaseReceiptAuditEventType.SubmittedForApproval or
            PurchaseReceiptAuditEventType.ReceivingFinished);
        var submitted = submittedIndex < 0 ? null : afterReturn[submittedIndex];
        var deltas = new Dictionary<int, LineDelta>();
        for (var index = 0; index < afterReturn.Length; index++)
        {
            var item = afterReturn[index];
            if (submittedIndex >= 0 && index >= submittedIndex &&
                (string.IsNullOrWhiteSpace(submitted!.TraceId) ||
                    !StringComparer.Ordinal.Equals(item.TraceId, submitted.TraceId)))
                continue;
            if (item.StockDocumentLineId is not > 0 || item.EventType is not (
                    PurchaseReceiptAuditEventType.PhysicalLineAdded or
                    PurchaseReceiptAuditEventType.PhysicalLineChanged or
                    PurchaseReceiptAuditEventType.PhysicalLineDeleted))
                continue;
            if (!TryReadValues(item.OldValuesJson, out var before) ||
                !TryReadValues(item.NewValuesJson, out var after))
                continue;
            if (item.EventType == PurchaseReceiptAuditEventType.PhysicalLineAdded && after.Count == 0 ||
                item.EventType == PurchaseReceiptAuditEventType.PhysicalLineDeleted && before.Count == 0 ||
                item.EventType == PurchaseReceiptAuditEventType.PhysicalLineChanged &&
                    !before.Keys.Intersect(after.Keys, StringComparer.Ordinal).Any())
                continue;

            var lineId = item.StockDocumentLineId.Value;
            if (!deltas.TryGetValue(lineId, out var delta))
            {
                var baselineExists = item.EventType != PurchaseReceiptAuditEventType.PhysicalLineAdded &&
                    !IsDeleted(before);
                delta = new LineDelta(baselineExists);
                deltas.Add(lineId, delta);
            }
            delta.Apply(item.EventType, before, after);
        }

        var review = new ReceiptRevisionReviewDto
        {
            ReturnedAtUtc = returned.OccurredAtUtc,
            ResubmittedAtUtc = submitted?.OccurredAtUtc,
            RemovedLineCount = deltas.Values.Count(delta => delta.BaselineExists && !delta.Exists)
        };
        foreach (var line in document.Lines)
        {
            if (!deltas.TryGetValue(line.Id, out var delta) || !delta.Exists) continue;
            var fields = delta.ChangedFields();
            if (!delta.BaselineExists)
            {
                line.RevisionChangeKind = "added";
                line.RevisionChangeSummary = "Sản phẩm bổ sung sau khi trả phiếu";
                review.AddedLineCount++;
            }
            else if (fields.Count > 0)
            {
                line.RevisionChangeKind = "changed";
                line.RevisionChangeSummary = BuildSummary(fields, delta);
                review.ChangedLineCount++;
            }
            else continue;
            line.RevisionChangedFields = fields;
            line.PreviousRevisionQuantity = delta.BaselineExists && delta.Before.TryGetValue(
                    nameof(StockDocumentLine.Quantity), out var quantity) &&
                quantity.ValueKind == JsonValueKind.Number && quantity.TryGetDecimal(out var value)
                    ? value : null;
        }
        document.RevisionReview = review;
    }

    private static bool TryReadValues(string? json, out Dictionary<string, JsonElement> values)
    {
        values = new(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var parsed = JsonDocument.Parse(json);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object) return false;
            foreach (var property in parsed.RootElement.EnumerateObject())
            {
                if ((ReviewFields.Contains(property.Name) || property.Name == nameof(StockDocumentLine.IsDeleted)) &&
                    property.Value.ValueKind is JsonValueKind.Number or JsonValueKind.String or
                        JsonValueKind.Null or JsonValueKind.True or JsonValueKind.False)
                    values[property.Name] = property.Value.Clone();
            }
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static bool IsDeleted(IReadOnlyDictionary<string, JsonElement> values)
        => values.TryGetValue(nameof(StockDocumentLine.IsDeleted), out var value) &&
            value.ValueKind == JsonValueKind.True;

    private static bool ValuesEqual(JsonElement before, JsonElement after)
        => before.ValueKind == JsonValueKind.Number && after.ValueKind == JsonValueKind.Number &&
            before.TryGetDecimal(out var previous) && after.TryGetDecimal(out var current)
                ? previous == current
                : before.ValueKind == after.ValueKind && before.ToString() == after.ToString();

    private static string BuildSummary(IReadOnlyList<string> fields, LineDelta delta)
    {
        var labels = new List<string>();
        if (fields.Contains(nameof(StockDocumentLine.Quantity)) &&
            delta.Before.TryGetValue(nameof(StockDocumentLine.Quantity), out var before) &&
            delta.After.TryGetValue(nameof(StockDocumentLine.Quantity), out var after))
            labels.Add($"Số lượng: {FormatQuantity(before)} → {FormatQuantity(after)}");
        foreach (var field in fields)
        {
            var label = field switch
            {
                nameof(StockDocumentLine.Quantity) => null,
                nameof(StockDocumentLine.ProductVariantId) => "Sản phẩm",
                nameof(StockDocumentLine.PurchaseOrderLineId) => "Dòng đặt hàng",
                nameof(StockDocumentLine.UnitId) or nameof(StockDocumentLine.ProductUnitConversionId) or
                    nameof(StockDocumentLine.Factor) => "Đơn vị / quy đổi",
                nameof(StockDocumentLine.UnitPriceBeforeVat) => "Giá nhập",
                nameof(StockDocumentLine.TaxId) or nameof(StockDocumentLine.TaxRate) => "Thuế VAT",
                nameof(StockDocumentLine.FreightAllocation) => "Phân bổ vận chuyển",
                nameof(StockDocumentLine.ShortageDisposition) or nameof(StockDocumentLine.ShortageReason)
                    => "Xử lý hàng thiếu",
                nameof(StockDocumentLine.Note) => "Ghi chú",
                _ => null
            };
            if (label is not null && !labels.Contains(label)) labels.Add(label);
        }
        return string.Join("; ", labels);
    }

    private static string FormatQuantity(JsonElement quantity)
        => quantity.ValueKind == JsonValueKind.Number && quantity.TryGetDecimal(out var value)
            ? value.ToString("0.###", CultureInfo.GetCultureInfo("vi-VN")) : quantity.ToString();

    private sealed class LineDelta(bool baselineExists)
    {
        public bool BaselineExists { get; } = baselineExists;
        public bool Exists { get; private set; } = baselineExists;
        public Dictionary<string, JsonElement> Before { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, JsonElement> After { get; } = new(StringComparer.Ordinal);

        public void Apply(PurchaseReceiptAuditEventType eventType,
            IReadOnlyDictionary<string, JsonElement> before,
            IReadOnlyDictionary<string, JsonElement> after)
        {
            foreach (var (field, value) in before)
            {
                Before.TryAdd(field, value);
                After.TryAdd(field, value);
            }
            foreach (var (field, value) in after) After[field] = value;
            if (eventType == PurchaseReceiptAuditEventType.PhysicalLineDeleted) Exists = false;
            else if (eventType == PurchaseReceiptAuditEventType.PhysicalLineAdded) Exists = true;
            else if (after.ContainsKey(nameof(StockDocumentLine.IsDeleted))) Exists = !IsDeleted(after);
        }

        public List<string> ChangedFields()
            => Before.Keys.Where(field => ReviewFields.Contains(field) &&
                    After.TryGetValue(field, out var value) && !ValuesEqual(Before[field], value))
                .OrderBy(field => field, StringComparer.Ordinal).ToList();
    }
}
