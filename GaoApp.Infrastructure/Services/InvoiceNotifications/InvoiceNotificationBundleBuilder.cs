using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Application.InvoiceNotifications;
using Microsoft.Extensions.Options;

namespace GaoApp.Infrastructure.Services.InvoiceNotifications;

/// <summary>Produces customer documents from the committed immutable snapshot, without querying live catalog prices.</summary>
public sealed class InvoiceNotificationBundleBuilder(IInvoiceFileStorage files, PrivateInvoicePreparedAssetStore assets,
    IOptions<InvoiceNotificationArtifactOptions> options, IInvoiceOfficialDocumentLinkFactory? officialLinks = null) : IInvoiceNotificationBundleBuilder, IInvoiceWorkItemBundleBuilder
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");
    public async Task<InvoicePreparedBundle> BuildAsync(InvoiceNotificationWorkItem item, CancellationToken ct)
    {
        if (item.Kind is not ("OfficialLink" or "CorrectionLink")) return await BuildAsync(item.Snapshot, ct);
        if (officialLinks is null) throw new InvoiceOfficialDocumentLinkNotConfiguredException();
        var snapshot = item.Snapshot;
        var artifact = snapshot.Artifact ?? throw new InvalidDataException("InvoiceArtifactNotReady");
        var url = await officialLinks.CreateAsync(item, new(artifact.FileName, artifact.ContentType, artifact.ByteLength, artifact.Sha256), ct);
        var fileKind = item.Kind == "CorrectionLink" ? "CorrectionPdf" : "OfficialPdf";
        var original = await BuildAsync(snapshot with { Kind = fileKind }, ct);
        var parts = original.Parts.Where(p => p.Kind == "Text").Select(p => p with { Text = p.Text?.Replace("Tệp đính kèm giữ nguyên nội dung gốc từ nhà cung cấp.", "Liên kết mở bản PDF chính thức đã lưu.") }).ToList();
        var linkText = "Xem hóa đơn (liên kết có thời hạn): " + url;
        if (linkText.EnumerateRunes().Count() > 4000) throw new InvalidDataException("InvoiceLinkTooLong");
        parts.Add(new("Text", linkText, null, null, null, null, null));
        if (parts.Count > 10) throw new InvalidDataException("InvoiceBundleLimitExceeded");
        return new(parts, Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(parts))).ToLowerInvariant());
    }

    public async Task<InvoicePreparedBundle> BuildAsync(InvoiceDocumentSnapshot snapshot, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (snapshot.SchemaVersion != 1 || snapshot.StoreId <= 0 || snapshot.CustomerId is not > 0 || snapshot.SourceId <= 0 ||
            string.IsNullOrWhiteSpace(snapshot.SourceVersion) || string.IsNullOrWhiteSpace(snapshot.Code) ||
            string.IsNullOrWhiteSpace(snapshot.StoreName) || snapshot.UnsupportedReason is not null)
            throw new InvalidDataException("InvoiceSnapshotUnavailable");
        var receipt = snapshot.Kind == "Receipt";
        var parts = new List<InvoicePreparedPart>();
        IReadOnlyList<string> lines;
        if (receipt)
        {
            ValidateReceipt(snapshot);
            lines = ReceiptLines(snapshot);
            foreach (var text in SplitText(lines)) parts.Add(new("Text", text, null, null, null, null, null));
            if (options.Value.IncludeReceiptImages)
            {
                if (!OperatingSystem.IsWindows()) throw new InvalidDataException("ReceiptImageRendererUnavailable");
                var images = InvoiceReceiptImageRenderer.Render(lines, 10 - parts.Count);
                for (var i = 0; i < images.Count; i++)
                    parts.Add(await assets.SaveAsync("Image", $"bill-{snapshot.SourceId}-{i + 1}.png", "image/png", images[i], ct));
            }
        }
        else
        {
            if (snapshot.Kind is not ("OfficialPdf" or "OfficialZipXml" or "CorrectionPdf" or "CorrectionZipXml" or "OfficialXml" or "CorrectionXml") ||
                snapshot.InvoiceId is not > 0 || snapshot.IssuedAtUtc is null ||
                snapshot.Status is not ("Issued" or "PdfDownloaded" or "ZipDownloaded" or "EmailSent"))
                throw new InvalidDataException("InvoiceNotIssued");
            if (snapshot.Kind.StartsWith("Correction", StringComparison.Ordinal) &&
                (snapshot.OriginalInvoiceId is not > 0 || string.IsNullOrWhiteSpace(snapshot.CorrectionType)))
                throw new InvalidDataException("InvoiceCorrectionEvidenceMissing");
            var artifact = snapshot.Artifact ?? throw new InvalidDataException("InvoiceArtifactNotReady");
            var extension = Path.GetExtension(artifact.FileName).ToLowerInvariant();
            if ((snapshot.Kind.EndsWith("Pdf", StringComparison.Ordinal) ? extension != ".pdf" : extension is not (".xml" or ".zip")) ||
                artifact.ByteLength is < 1 or > 20L * 1024 * 1024 || artifact.Sha256.Length != 64)
                throw new InvalidDataException("InvoiceArtifactInvalid");
            var stored = files is IBoundedInvoiceFileStorage bounded
                ? await bounded.ReadBoundedAsync(artifact.StoragePath, 20L * 1024 * 1024, ct)
                : await files.ReadAsync(artifact.StoragePath, ct);
            if (stored is null || stored.Value.FileName != artifact.FileName || stored.Value.ContentType != artifact.ContentType ||
                stored.Value.Bytes.LongLength != artifact.ByteLength ||
                !Convert.ToHexString(SHA256.HashData(stored.Value.Bytes)).Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("InvoiceArtifactMissingOrChanged");
            var attachmentName = artifact.FileName;
            var attachmentBytes = stored.Value.Bytes;
            var attachmentType = artifact.ContentType;
            if ((snapshot.Kind is "OfficialXml" or "CorrectionXml") && extension == ".zip")
            {
                (attachmentName, attachmentBytes) = await InvoiceOriginalXmlExtractor.ExtractAsync(artifact.FileName, stored.Value.Bytes, ct);
                attachmentType = "application/xml";
            }
            var label = snapshot.Kind.StartsWith("Correction", StringComparison.Ordinal)
                ? snapshot.CorrectionType == "Replacement" ? "HÓA ĐƠN THAY THẾ" : "HÓA ĐƠN ĐIỀU CHỈNH"
                : "HÓA ĐƠN ĐIỆN TỬ";
            var heading = new List<string> { Clean(snapshot.StoreName), $"{label}: {Clean(snapshot.Code)}",
                $"Phát hành: {LocalDate(snapshot.IssuedAtUtc.Value)}" };
            if (!string.IsNullOrWhiteSpace(snapshot.OrderCode)) heading.Add($"Đơn hàng: {Clean(snapshot.OrderCode)}");
            if (snapshot.OriginalInvoiceId is { } original)
                heading.Add(string.IsNullOrWhiteSpace(snapshot.OriginalInvoiceCode) ? "Có liên kết hóa đơn gốc trong chứng từ đính kèm." : $"Hóa đơn gốc: {Clean(snapshot.OriginalInvoiceCode)}");
            if (!string.IsNullOrWhiteSpace(snapshot.LegalEntityName)) heading.Add($"Đơn vị phát hành: {Clean(snapshot.LegalEntityName)}");
            heading.Add("Tệp đính kèm giữ nguyên nội dung gốc từ nhà cung cấp.");
            foreach (var text in SplitText(heading)) parts.Add(new("Text", text, null, null, null, null, null));
            parts.Add(await assets.SaveAsync("File", attachmentName, attachmentType, attachmentBytes, ct));
        }
        if (parts.Count is < 1 or > 10 || parts.Sum(p => p.ByteLength ?? 0L) > 50L * 1024 * 1024)
            throw new InvalidDataException("InvoiceBundleLimitExceeded");
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(parts.Select(p => new
            { p.Kind, p.Text, p.FileName, p.ContentType, p.ByteLength, p.Sha256 })))).ToLowerInvariant();
        return new(parts, fingerprint);
    }

    public static void ValidateReceipt(InvoiceDocumentSnapshot s)
    {
        if (s.SourceType != "Order" || s.Status != "Completed" || s.FinalizedAtUtc is null || s.OrderId is not > 0 ||
            s.Lines.Count is < 1 or > 1000 || s.Payments.Count > 1000 || s.Lines.Select(l => l.Id).Distinct().Count() != s.Lines.Count)
            throw new InvalidDataException("ReceiptSnapshotInvalid");
        decimal expectedSubtotal = 0;
        foreach (var line in s.Lines)
        {
            if (line.Id <= 0 || string.IsNullOrWhiteSpace(line.Name) || line.Quantity <= 0 || line.UnitPrice < 0 || line.LineDiscount < 0 || line.PromotionDiscount < 0 ||
                line.LineTotal != Money(Money(line.Quantity * line.UnitPrice) - line.LineDiscount - line.PromotionDiscount))
                throw new InvalidDataException("ReceiptLineTotalsMismatch");
            expectedSubtotal += line.PromotionType == "ComboFixedPrice" ? Money(line.Quantity * line.UnitPrice) : line.Quantity * line.UnitPrice;
        }
        if (s.Subtotal != Money(expectedSubtotal) || s.LineDiscountTotal != Money(s.Lines.Sum(l => l.LineDiscount)) ||
            s.PromotionDiscountTotal != Money(s.Lines.Sum(l => l.PromotionDiscount)) ||
            s.DiscountTotal != Money(s.LineDiscountTotal + s.PromotionDiscountTotal + s.ComboDiscountTotal + s.OrderDiscount + s.VoucherDiscountTotal) ||
            s.GrandTotal != Money(Math.Max(s.Subtotal - s.DiscountTotal, 0)) ||
            s.Payments.Any(p => p.Amount < 0 || string.IsNullOrWhiteSpace(p.Method)) ||
            s.PaidTotal != Money(s.Payments.Sum(p => p.Amount) + s.DepositAmount) ||
            s.ChangeDue != Math.Max(s.PaidTotal - s.GrandTotal, 0) || s.BalanceDue != Math.Max(s.GrandTotal - s.PaidTotal, 0))
            throw new InvalidDataException("ReceiptTotalsMismatch");
    }
    private static decimal Money(decimal value) => decimal.Round(value, 0, MidpointRounding.AwayFromZero);
    private static string PaymentLabel(string value) => value switch { "Cash" => "tiền mặt", "BankTransfer" or "Bank" => "chuyển khoản", "Card" => "thẻ", "EWallet" => "ví điện tử", "Other" => "khác", _ => Clean(value) };
    private static string Exact(decimal value) => value.ToString("0.############################", Vi);
    private static string Clean(string text) => string.Concat(text.Select(c => char.IsControl(c) ? ' ' : c));
    private static string LocalDate(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).AddHours(7).ToString("dd/MM/yyyy HH:mm", Vi) + " (UTC+7)";
    public static IReadOnlyList<string> ReceiptLines(InvoiceDocumentSnapshot s)
    {
        var lines = new List<string> { Clean(s.StoreName), $"PHIẾU BÁN HÀNG: {Clean(s.Code)}", $"Thời điểm chốt: {LocalDate(s.FinalizedAtUtc!.Value)}",
            "Phiếu bán hàng, không phải hóa đơn điện tử.", $"Khách: {Clean(s.CustomerName ?? "Khách hàng")}", "" };
        var index = 0;
        foreach (var line in s.Lines)
        {
            lines.Add($"{++index}. {Clean(line.Name)}" + (line.IsPromotionGift ? " (hàng tặng)" : ""));
            lines.Add($"   {Exact(line.Quantity)} {Clean(line.Unit ?? "")} · Đơn giá {Exact(line.UnitPrice)} · Thành tiền {Exact(line.LineTotal)} đ");
            if (line.LineDiscount != 0) lines.Add($"   Giảm trực tiếp dòng: {Exact(line.LineDiscount)} đ");
            if (line.PromotionDiscount != 0) lines.Add($"   Khuyến mãi dòng: {Exact(line.PromotionDiscount)} đ");
        }
        lines.Add(""); lines.Add($"Tiền hàng: {Exact(s.Subtotal)} đ");
        lines.Add($"Tổng giảm: {Exact(s.DiscountTotal)} đ");
        lines.Add($"  Giảm dòng: {Exact(s.LineDiscountTotal)} · KM: {Exact(s.PromotionDiscountTotal)} · Combo: {Exact(s.ComboDiscountTotal)} đ");
        lines.Add($"  Giảm đơn: {Exact(s.OrderDiscount)} · Voucher: {Exact(s.VoucherDiscountTotal)} đ");
        lines.Add($"TỔNG THANH TOÁN: {Exact(s.GrandTotal)} đ");
        foreach (var p in s.Payments) lines.Add($"Thanh toán {PaymentLabel(p.Method)}: {Exact(p.Amount)} đ" + (string.IsNullOrEmpty(p.Reference) ? "" : $" · {Clean(p.Reference)}"));
        if (s.DepositAmount != 0) lines.Add($"Tiền đặt cọc đã ghi nhận: {Exact(s.DepositAmount)} đ");
        lines.Add($"Đã ghi nhận: {Exact(s.PaidTotal)} đ · Tiền thừa: {Exact(s.ChangeDue)} đ");
        lines.Add($"Còn phải thu: {Exact(s.BalanceDue)} đ");
        lines.Add(s.CurrentPoints is { } points ? $"Điểm khả dụng tại thời điểm chốt: {Exact(points)}" : "Điểm khả dụng tại thời điểm chốt: chưa xác định.");
        return lines;
    }
    internal static IReadOnlyList<string> SplitText(IEnumerable<string> lines)
    {
        var result = new List<string>(); var current = new StringBuilder(); var count = 0;
        foreach (var line in lines)
        {
            foreach (var rune in (line + "\n").EnumerateRunes())
            {
                if (count == 4000) { result.Add(current.ToString()); current.Clear(); count = 0; }
                current.Append(rune.ToString()); count++;
                if (result.Count >= 10) throw new InvalidDataException("InvoiceBundleLimitExceeded");
            }
        }
        if (current.Length > 0 && !string.IsNullOrWhiteSpace(current.ToString())) result.Add(current.ToString());
        if (result.Count > 10) throw new InvalidDataException("InvoiceBundleLimitExceeded");
        return result;
    }
}
