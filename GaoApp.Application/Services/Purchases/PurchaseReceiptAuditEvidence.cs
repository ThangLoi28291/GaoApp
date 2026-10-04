using System.Runtime.CompilerServices;
using System.Text.Json;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Purchases;

/// <summary>
/// Canonical allowlists and workflow intent used to build receipt evidence.
/// No entity graph, request body, concurrency token, or secret is serialized.
/// </summary>
public static class PurchaseReceiptAuditEvidence
{
    public static readonly IReadOnlyList<string> HeaderFields =
    [
        nameof(StockDocument.WarehouseId),
        nameof(StockDocument.SupplierId),
        nameof(StockDocument.ReceiptSource),
        nameof(StockDocument.PurchaseOrderId),
        nameof(StockDocument.DocumentDate),
        nameof(StockDocument.Note),
        nameof(StockDocument.DirectReceiptReason),
        nameof(StockDocument.HasVat),
        nameof(StockDocument.IncludeVatInInventoryCost),
        nameof(StockDocument.SubtotalBeforeVat),
        nameof(StockDocument.VatAmount),
        nameof(StockDocument.HasFreight),
        nameof(StockDocument.CapitalizeFreightInInventoryCost),
        nameof(StockDocument.FreightTotal),
        nameof(StockDocument.FreightPayeeName),
        nameof(StockDocument.FreightNote),
        nameof(StockDocument.IsFreightPaid),
        nameof(StockDocument.IsMerchandisePaid),
        nameof(StockDocument.MerchandisePayeeName),
        nameof(StockDocument.TotalAmount),
        nameof(StockDocument.Status),
        nameof(StockDocument.ApprovalNote),
        nameof(StockDocument.WaitForInputInvoice),
        nameof(StockDocument.HasRevisionRequest),
        nameof(StockDocument.RevisionRequestNote),
        nameof(StockDocument.RevisionRequestedAtUtc),
        nameof(StockDocument.RevisionRequestedByUserId),
        nameof(StockDocument.RevisionResolvedAtUtc),
        nameof(StockDocument.RevisionResolvedByUserId),
        nameof(StockDocument.SubmittedAtUtc),
        nameof(StockDocument.SubmittedByUserId),
        nameof(StockDocument.ApprovedAtUtc),
        nameof(StockDocument.ApprovedByUserId),
        nameof(StockDocument.ConfirmedAtUtc),
        nameof(StockDocument.ConfirmedByUserId),
        nameof(StockDocument.ConfirmedLegalEntityId)
    ];

    public static readonly IReadOnlyList<string> LineFields =
    [
        nameof(StockDocumentLine.ProductVariantId),
        nameof(StockDocumentLine.PurchaseOrderLineId),
        nameof(StockDocumentLine.ProductUnitConversionId),
        nameof(StockDocumentLine.UnitId),
        nameof(StockDocumentLine.Factor),
        nameof(StockDocumentLine.Quantity),
        nameof(StockDocumentLine.BaseQuantity),
        nameof(StockDocumentLine.UnitPriceBeforeVat),
        nameof(StockDocumentLine.TaxId),
        nameof(StockDocumentLine.TaxRate),
        nameof(StockDocumentLine.VatAmount),
        nameof(StockDocumentLine.UnitPriceAfterVat),
        nameof(StockDocumentLine.UnitCost),
        nameof(StockDocumentLine.LineTotal),
        nameof(StockDocumentLine.FreightAllocation),
        nameof(StockDocumentLine.ShortageDisposition),
        nameof(StockDocumentLine.ShortageReason),
        nameof(StockDocumentLine.Note)
    ];

    private static readonly ConditionalWeakTable<StockDocument, WorkflowIntent>
        WorkflowIntents = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null
    };

    public static void MarkWorkflowEvent(
        StockDocument document,
        PurchaseReceiptAuditEventType eventType,
        string? reason = null,
        string? note = null,
        IReadOnlyDictionary<string, object?>? evidenceValues = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var normalizedReason = NormalizeText(reason);
        if (eventType is PurchaseReceiptAuditEventType.RevisionRequested or
                PurchaseReceiptAuditEventType.RevisionReturnedForEditing or
                PurchaseReceiptAuditEventType.ReceiptRejected &&
            normalizedReason is null)
        {
            throw new InvalidOperationException(
                $"Audit reason is required for {eventType}.");
        }

        WorkflowIntents.Remove(document);
        WorkflowIntents.Add(
            document,
            new WorkflowIntent(
                eventType,
                normalizedReason,
                NormalizeText(note),
                evidenceValues is null
                    ? new Dictionary<string, object?>(StringComparer.Ordinal)
                    : new Dictionary<string, object?>(
                        evidenceValues,
                        StringComparer.Ordinal)));
    }

    public static WorkflowIntent? GetWorkflowIntent(StockDocument document)
        => WorkflowIntents.TryGetValue(document, out var intent)
            ? intent
            : null;

    public static void ClearWorkflowIntent(StockDocument document)
        => WorkflowIntents.Remove(document);

    public static string SerializeChangedFields(IEnumerable<string> fields)
        => JsonSerializer.Serialize(
            fields.Distinct(StringComparer.Ordinal)
                .OrderBy(static field => field, StringComparer.Ordinal),
            JsonOptions);

    public static string SerializeValues(
        IEnumerable<KeyValuePair<string, object?>> values)
        => JsonSerializer.Serialize(
            values.OrderBy(static item => item.Key, StringComparer.Ordinal)
                .ToDictionary(
                    static item => item.Key,
                    static item => NormalizeValue(item.Key, item.Value),
                    StringComparer.Ordinal),
            JsonOptions);

    public static bool ValuesEqual(
        string fieldName,
        object? left,
        object? right)
        => Equals(
            NormalizeValue(fieldName, left),
            NormalizeValue(fieldName, right));

    private static object? NormalizeValue(string fieldName, object? value)
        => value is string text
            ? text[..Math.Min(text.Length, 1000)]
            : value is Enum enumValue
            ? enumValue.ToString()
            : value is DateTime dateTime
                ? NormalizeDateTime(fieldName, dateTime)
                : value;

    private static DateTime NormalizeDateTime(
        string fieldName,
        DateTime value)
    {
        // SQL Server datetime2 does not retain DateTime.Kind. Fields whose
        // names declare UTC semantics therefore need their kind restored
        // without shifting the stored clock value.
        if (fieldName.EndsWith("Utc", StringComparison.Ordinal))
        {
            return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        // DocumentDate is a receipt calendar value, not an instant. Preserve
        // its date/time components without applying the server time zone.
        if (fieldName == nameof(StockDocument.DocumentDate))
        {
            return DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        }

        throw new InvalidOperationException(
            $"Audit DateTime semantics are not defined for {fieldName}.");
    }

    private static string? NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public sealed record WorkflowIntent(
        PurchaseReceiptAuditEventType EventType,
        string? Reason,
        string? Note,
        IReadOnlyDictionary<string, object?> EvidenceValues);
}
