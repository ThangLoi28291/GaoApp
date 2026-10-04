using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Inventory;

public static class ReceiptInvoiceFollowUp
{
    public static string Resolve(bool? wait, bool linked, InputInvoiceReconciliationState? reconciliation, bool reviewed = false)
        => linked && reviewed ? "Reviewed" : linked ? reconciliation is InputInvoiceReconciliationState.Matched or InputInvoiceReconciliationState.AcceptedMismatch
            ? "Complete" : "NeedsReview"
            : wait == true ? "Waiting" : wait == false ? "NotExpected" : "Unclassified";

    public static bool IsReviewCurrent(string? evidenceJson, int? mapId, string? fingerprint)
    {
        if (string.IsNullOrEmpty(evidenceJson) || mapId == null || string.IsNullOrEmpty(fingerprint)) return false;
        try {
            using var evidence = JsonDocument.Parse(evidenceJson);
            return evidence.RootElement.ValueKind == JsonValueKind.Object &&
                evidence.RootElement.TryGetProperty("MapId", out var map) && map.ValueKind == JsonValueKind.Number && map.TryGetInt32(out var id) && id == mapId &&
                evidence.RootElement.TryGetProperty("EvidenceFingerprint", out var hash) && hash.ValueKind == JsonValueKind.String && hash.GetString() == fingerprint;
        } catch (JsonException) { return false; }
    }
}

public sealed record ReceiptInvoiceFollowUpDto(string State, bool HasLinkedInvoice, bool IsConfirmed,
    string RowVersion, int WaitingDays, PurchaseReceiptSource ReceiptSource)
{
    public int? MapId { get; init; }
    public string? EvidenceFingerprint { get; init; }
    public decimal? ReceiptGoodsTotal { get; init; }
    public decimal? XmlPaymentAmount { get; init; }
    public int UnmatchedDetailCount { get; init; }
    public string? ReviewReason { get; init; }
    public string? ReviewedBy { get; init; }
    public DateTime? ReviewedAtUtc { get; init; }
}

public sealed class ReviewReceiptInvoiceRequest : EndReceiptInvoiceWaitRequest
{
    [Range(1, int.MaxValue)] public int MapId { get; set; }
    [Required, StringLength(64, MinimumLength = 1)] public string EvidenceFingerprint { get; set; } = string.Empty;
}

public class EndReceiptInvoiceWaitRequest
{
    [Required] public string RowVersion { get; set; } = string.Empty;
    [Required, StringLength(500)] public string Reason { get; set; } = string.Empty;
}
