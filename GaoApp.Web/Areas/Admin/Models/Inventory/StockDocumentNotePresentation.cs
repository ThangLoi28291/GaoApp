namespace GaoApp.Web.Areas.Admin.Models.Inventory;

public sealed record StockDocumentNoteField(string Label, string Value, string? SummaryText = null);

/// <summary>Formats imported notes for display without changing the stored note.</summary>
public sealed class StockDocumentNotePresentation
{
    public string OriginalNote { get; private init; } = string.Empty;
    public string Text { get; private init; } = string.Empty;
    public bool IsLegacy { get; private init; }
    public bool HasSourceNote { get; private init; }
    public IReadOnlyList<StockDocumentNoteField> Fields { get; private init; } = [];
    public string Summary => string.Join(" • ", Fields.Select(item => item.SummaryText ?? item.Value));

    public static StockDocumentNotePresentation FromNote(string? note)
    {
        var original = note ?? string.Empty;
        var content = original.Trim();
        const string prefix = "LEGACY_HDN|";
        if (!content.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return new() { OriginalNote = original, Text = original };

        // SourceNote is the final field written by the importer. Its value may
        // itself contain pipes, equals signs or newlines and must stay intact.
        const string sourceNoteMarker = "|SourceNote=";
        var sourceNoteIndex = original.IndexOf(sourceNoteMarker, StringComparison.OrdinalIgnoreCase);
        var metadata = sourceNoteIndex >= 0 ? original[..sourceNoteIndex].Trim() : content;
        var sourceNote = sourceNoteIndex >= 0 ? original[(sourceNoteIndex + sourceNoteMarker.Length)..] : null;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var segment in metadata.Split('|').Skip(1))
        {
            var separator = segment.IndexOf('=');
            if (separator <= 0) continue;
            var key = segment[..separator].Trim();
            var value = segment[(separator + 1)..].Trim();
            if (value.Length > 0 && !value.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                values.TryAdd(key, value);
        }

        var fields = new List<StockDocumentNoteField>();
        if (values.TryGetValue("MaHD", out var documentNumber))
            fields.Add(new("Mã phiếu cũ", documentNumber, $"Phiếu cũ {documentNumber}"));
        if (values.TryGetValue("LegacyInputUserName", out var inputUserName))
            fields.Add(new("Người nhập (hệ thống cũ)", inputUserName, $"Người nhập: {inputUserName}"));
        else if (values.TryGetValue("LegacyInputUserId", out var inputUserId))
            fields.Add(new("Người nhập (hệ thống cũ)", $"Mã {inputUserId}", $"Người nhập: Mã {inputUserId}"));
        if (values.TryGetValue("LegacyStatus", out var status))
            fields.Add(new("Trạng thái lúc chuyển dữ liệu", status switch
            {
                "0" => "Chờ duyệt",
                "1" => "Đã duyệt",
                _ => $"Chưa xác định (mã {status})"
            }));
        if (values.TryGetValue("PaymentState", out var paymentState))
            fields.Add(new("Thanh toán trên dữ liệu cũ", paymentState.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase)
                ? "Chưa xác định từ dữ liệu cũ"
                : $"Chưa xác định (mã {paymentState})",
                paymentState.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase)
                    ? "Thanh toán chưa xác định"
                    : $"Thanh toán chưa xác định (mã {paymentState})"));

        return new()
        {
            OriginalNote = original,
            Text = string.IsNullOrWhiteSpace(sourceNote) ? "Phiếu nhập được chuyển từ hệ thống cũ." : sourceNote,
            IsLegacy = true,
            HasSourceNote = !string.IsNullOrWhiteSpace(sourceNote),
            Fields = fields
        };
    }
}
