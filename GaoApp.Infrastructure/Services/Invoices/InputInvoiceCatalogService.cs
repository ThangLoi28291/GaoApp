using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Helpers;
using GaoApp.Application.DTOs.Inventory.InputInvoices;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Invoices;

public sealed class InvoiceCatalogFilter
{
    public int? Year { get; set; }
    public int? Month { get; set; }
    public string? Search { get; set; }
    public string? Supplier { get; set; }
    public int? Review { get; set; }
    public int? Kind { get; set; }
    public bool? Linked { get; set; }
    public int Page { get; set; } = 1;
}
public sealed class InvoiceCatalogReviewRequest
{
    public int Status { get; set; }
    public string? Note { get; set; }
    public string RowVersion { get; set; } = "";
}
public sealed class InvoiceCatalogSyncResult
{
    public int Added { get; set; }
    public int Existing { get; set; }
    public int Invalid { get; set; }
    public int OutsideScope { get; set; }
    public int Conflicts { get; set; }
    public int FolderMismatches { get; set; }
    public int Unavailable { get; set; }
}

public sealed class InputInvoiceCatalogService(AppDbContext db,
    FileSystemInputInvoiceDocumentLibrary library, IInputInvoiceXmlDocumentParser parser)
{
    private IQueryable<InputInvoiceLibraryEntry> Scoped(int storeId) => db.Set<InputInvoiceLibraryEntry>()
        .Where(x => x.StoreId == storeId && db.LegalEntities.Count(l => l.StoreId == storeId && l.IsActive && l.NormalizedTaxCode == x.BuyerTaxCode) == 1);

    private IQueryable<InputInvoiceLibraryEntry> Linked(int storeId) => Scoped(storeId).Where(e =>
        db.InputInvoiceHeads.Any(h => h.StoreId == storeId && h.NormalizedSellerTaxCode == e.SellerTaxCode &&
            h.NormalizedInvoiceSeries == e.Series && h.NormalizedInvoiceNumber == e.Number && h.InvoiceIdentityDate == e.InvoiceDate &&
            h.StockDocumentMaps.Any(m => m.StoreId == storeId)));

    public async Task<object> ListAsync(int storeId, InvoiceCatalogFilter f, CancellationToken ct)
    {
        if (f.Year is < 2000 or > 9999 || f.Month is < 1 or > 12 || f.Review is < 0 or > 3 || f.Kind is < 0 or > 3)
            throw new BusinessRuleException("Bộ lọc không hợp lệ.");
        var q = Scoped(storeId).AsNoTracking();
        var linkedIds = Linked(storeId).Select(x => x.Id);
        if (f.Year.HasValue) q = q.Where(x => x.InvoiceDate.Year == f.Year);
        if (f.Month.HasValue) q = q.Where(x => x.InvoiceDate.Month == f.Month);
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim();
            q = q.Where(x => x.Number.Contains(s) || x.Series.Contains(s) || x.SellerName.Contains(s) || x.SellerTaxCode.Contains(s) || x.BuyerName.Contains(s));
        }
        if (!string.IsNullOrWhiteSpace(f.Supplier)) q = q.Where(x => x.SellerTaxCode == f.Supplier);
        if (f.Review.HasValue) q = q.Where(x => x.ReviewStatus == f.Review);
        if (f.Kind.HasValue) q = q.Where(x => x.InvoiceKind == f.Kind);
        if (f.Linked.HasValue) q = q.Where(x => linkedIds.Contains(x.Id) == f.Linked.Value);
        var total = await q.CountAsync(ct);
        var summary = await q.GroupBy(x => x.InvoiceKind).Select(g => new { Kind = g.Key, Count = g.Count(), Total = g.Sum(x => x.Total) }).ToListAsync(ct);
        var uncheckedCount = await q.CountAsync(x => x.ReviewStatus == 0, ct);
        var unlinked = await q.CountAsync(x => !linkedIds.Contains(x.Id), ct);
        var page = Math.Clamp(f.Page, 1, Math.Max(1, (total + 29) / 30));
        var rows = await q.OrderByDescending(x => x.InvoiceDate).ThenByDescending(x => x.Id).Skip((page - 1) * 30).Take(30)
            .Select(x => new { x.Id, x.Series, x.Number, x.InvoiceDate, x.SellerTaxCode, x.SellerName, x.BuyerName,
                x.Total, x.Tax, x.InvoiceKind, x.ReviewStatus, x.HasPdf, x.ReviewedAtUtc,
                FolderMismatch = x.SourceSupplierTaxCode != null && x.SourceSupplierTaxCode != x.SellerTaxCode,
                Linked = linkedIds.Contains(x.Id) }).ToListAsync(ct);
        var suppliers = await Scoped(storeId).GroupBy(x => x.SellerTaxCode)
            .Select(g => new { TaxCode = g.Key, Name = g.Max(x => x.SellerName) }).OrderBy(x => x.Name).ToListAsync(ct);
        return new { rows, total, page, pages = Math.Max(1, (total + 29) / 30), summary, uncheckedCount, unlinked, suppliers };
    }

    public async Task<object> DetailAsync(int storeId, int id, CancellationToken ct)
    {
        var e = await RequireAsync(storeId, id, ct);
        var receipts = await db.StockDocumentInputInvoiceMaps.Where(m => m.StoreId == storeId &&
            m.InputInvoiceHead.StoreId == storeId && m.StockDocument.StoreId == storeId &&
            m.InputInvoiceHead.NormalizedSellerTaxCode == e.SellerTaxCode && m.InputInvoiceHead.NormalizedInvoiceSeries == e.Series &&
            m.InputInvoiceHead.NormalizedInvoiceNumber == e.Number && m.InputInvoiceHead.InvoiceIdentityDate == e.InvoiceDate)
            .Select(m => new { m.StockDocumentId, m.StockDocument.DocumentNo, m.StockDocument.DocumentTitle }).ToListAsync(ct);
        var history = await db.Set<InputInvoiceLibraryReview>().Where(x => x.StoreId == storeId && x.InputInvoiceLibraryEntryId == id)
            .OrderByDescending(x => x.Id).Select(x => new { x.Status, x.PreviousStatus, x.Note, x.ActorId, x.OccurredAtUtc,
                ActorName = db.UserInStores.Where(u => u.StoreId == storeId && u.UserId == x.ActorId).Select(u => u.User.FullName).FirstOrDefault() }).Take(100).ToListAsync(ct);
        return new { e.Id, e.Series, e.Number, e.InvoiceDate, e.SellerName, e.SellerTaxCode, e.BuyerName,
            e.BuyerTaxCode, e.Total, e.BeforeTax, e.Tax, e.InvoiceKind, e.RelatedInvoice, e.HasPdf, e.SourceSupplierTaxCode,
            FolderMismatch = e.SourceSupplierTaxCode != null && e.SourceSupplierTaxCode != e.SellerTaxCode,
            e.ReviewStatus, e.ReviewNote, e.ReviewedBy, e.ReviewedAtUtc, rowVersion = Convert.ToBase64String(e.RowVersion), receipts, history };
    }

    public async Task ReviewAsync(int storeId, int id, int actorId, InvoiceCatalogReviewRequest request, CancellationToken ct)
    {
        if (request.Status is < 0 or > 3 || (request.Note?.Length ?? 0) > 1000 ||
            (request.Status == 3 && string.IsNullOrWhiteSpace(request.Note)))
            throw new BusinessRuleException("Chọn trạng thái hợp lệ. Không ghi nhận / cần làm rõ bắt buộc ghi lý do (tối đa 1.000 ký tự).");
        var e = await RequireAsync(storeId, id, ct);
        if (Convert.ToBase64String(e.RowVersion) != request.RowVersion)
            throw new DbUpdateConcurrencyException("Hóa đơn đã được cập nhật. Mở lại để kiểm tra trước khi lưu.");
        await ReadVerifiedXmlAsync(e, ct);
        var now = DateTime.UtcNow;
        db.Add(new InputInvoiceLibraryReview { StoreId = storeId, Entry = e, PreviousStatus = e.ReviewStatus,
            Status = request.Status, Note = request.Note?.Trim(), ActorId = actorId, OccurredAtUtc = now });
        e.ReviewStatus = request.Status; e.ReviewNote = request.Note?.Trim(); e.ReviewedAtUtc = now; e.ReviewedBy = actorId;
        await db.SaveChangesAsync(ct);
    }

    public async Task<InvoiceCatalogSyncResult> SyncAsync(int storeId, int? year, int? month, CancellationToken ct)
    {
        var result = new InvoiceCatalogSyncResult();
        var allowed = await AllowedBuyersAsync(storeId, ct);
        await foreach (var candidate in library.EnumerateCatalogAsync(year, month, ct))
        {
            if (!candidate.XmlValid) { result.Invalid++; continue; }
            if (!allowed.Contains(TaxCodeIdentityNormalizer.Normalize(candidate.BuyerTaxCode) ?? "")) { result.OutsideScope++; continue; }
            byte[] bytes;
            try { bytes = await library.GetXmlBytesAsync(candidate.SourceSupplierTaxCode!, candidate.DocumentKey, ct); }
            catch (Exception ex) when (ex is BusinessRuleException or FileNotFoundException or DirectoryNotFoundException)
            {
                // Files may be moved/replaced while the external importer is running.
                // Keep previously indexed entries and continue with the remaining candidates.
                result.Unavailable++; continue;
            }
            if (!candidate.FolderTaxCodeMatches) result.FolderMismatches++;
            try { await IndexAsync(storeId, candidate, bytes, result, ct); }
            catch (BusinessRuleException) { result.Invalid++; }
        }
        return result;
    }

    public async Task<InvoiceCatalogSyncResult> UploadAsync(int storeId, byte[] xml, byte[]? pdf, CancellationToken ct)
    {
        var invoice = parser.Parse(xml);
        if (!(await AllowedBuyersAsync(storeId, ct)).Contains(TaxCodeIdentityNormalizer.Normalize(invoice.BuyerTaxCode) ?? ""))
            throw new BusinessRuleException("MST người mua phải khớp duy nhất một chủ thể đang hoạt động của cửa hàng.");
        if (pdf is { Length: > 0 } && (pdf.Length > 50 * 1024 * 1024 || pdf.Length < 5 || Encoding.ASCII.GetString(pdf, 0, 5) != "%PDF-"))
            throw new BusinessRuleException("File đính kèm không phải PDF hợp lệ hoặc quá 50 MB.");
        var result = new InvoiceCatalogSyncResult();
        var identity = Identity(invoice);
        var existingHash = await db.Set<InputInvoiceLibraryEntry>().Where(x => x.StoreId == storeId && x.IdentityHash == identity)
            .Select(x => x.XmlHash).SingleOrDefaultAsync(ct);
        if (existingHash != null && existingHash != Hash(xml)) { result.Conflicts++; return result; }
        var candidate = await library.StoreCatalogAsync(xml, pdf, ct);
        await IndexAsync(storeId, candidate, xml, result, ct);
        return result;
    }

    private async Task IndexAsync(int storeId, InputInvoicePickerCandidateDto candidate, byte[] bytes, InvoiceCatalogSyncResult result, CancellationToken ct)
    {
        var invoice = parser.Parse(bytes);
        // Recheck the final bytes; a source file can change after enumeration.
        var buyerTaxCode = TaxCodeIdentityNormalizer.Normalize(invoice.BuyerTaxCode);
        if (string.IsNullOrEmpty(buyerTaxCode) || await db.LegalEntities.CountAsync(x => x.StoreId == storeId && x.IsActive && x.NormalizedTaxCode == buyerTaxCode, ct) != 1)
        { result.OutsideScope++; return; }
        var identity = Identity(invoice);
        var hash = Hash(bytes);
        var e = await db.Set<InputInvoiceLibraryEntry>().SingleOrDefaultAsync(x => x.StoreId == storeId && x.IdentityHash == identity, ct);
        if (e != null)
        {
            if (e.XmlHash != hash) { result.Conflicts++; return; }
            if (candidate.HasPdf && !e.HasPdf)
            { e.DocumentKey = candidate.DocumentKey; e.SourceSupplierTaxCode = candidate.SourceSupplierTaxCode; e.HasPdf = true; await db.SaveChangesAsync(ct); }
            result.Existing++; return;
        }
        var (kind, related) = ReadRelation(bytes);
        e = new InputInvoiceLibraryEntry { StoreId = storeId, IdentityHash = identity, XmlHash = hash,
            DocumentKey = candidate.DocumentKey, SourceSupplierTaxCode = candidate.SourceSupplierTaxCode,
            SellerTaxCode = invoice.NormalizedSellerTaxCode!, SellerName = invoice.SellerName ?? "",
            BuyerTaxCode = TaxCodeIdentityNormalizer.Normalize(invoice.BuyerTaxCode)!, BuyerName = invoice.BuyerName ?? "",
            Series = invoice.NormalizedInvoiceSeries!, Number = invoice.NormalizedInvoiceNumber!, InvoiceDate = invoice.InvoiceIdentityDate!.Value,
            BeforeTax = invoice.TotalBeforeTax, Tax = invoice.TotalTaxAmount, Total = invoice.TotalPaymentAmount,
            InvoiceKind = kind, RelatedInvoice = related, HasPdf = candidate.HasPdf };
        db.Add(e);
        try { await db.SaveChangesAsync(ct); result.Added++; }
        catch (DbUpdateException) { db.Entry(e).State = EntityState.Detached; throw; }
        db.Entry(e).State = EntityState.Detached;
    }

    public static (int Kind, string? Related) ReadRelation(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 12 * 1024 * 1024 });
        var doc = XDocument.Load(reader);
        var relation = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "TTChung")?.Elements().FirstOrDefault(x => x.Name.LocalName == "TTHDLQuan");
        if (relation == null) return (0, null);
        string Read(string name) => relation.Elements().FirstOrDefault(x => x.Name.LocalName == name)?.Value.Trim() ?? "";
        var kind = Read("TCHDon") switch { "1" => 1, "2" => 2, _ => 3 };
        var related = string.Join(" · ", new[] { Read("KHMSHDCLQuan"), Read("KHHDCLQuan"), Read("SHDCLQuan"), Read("NLHDCLQuan") }.Where(x => x.Length > 0));
        return (kind, related.Length > 500 ? related[..500] : related);
    }

    public async Task<InputInvoicePdfPreviewDto> PdfAsync(int storeId, int id, CancellationToken ct)
    {
        var e = await RequireAsync(storeId, id, ct);
        await ReadVerifiedXmlAsync(e, ct);
        return await library.GetPdfAsync(e.SourceSupplierTaxCode ?? e.SellerTaxCode, e.DocumentKey, ct);
    }
    public async Task<byte[]> XmlAsync(int storeId, int id, CancellationToken ct) => await ReadVerifiedXmlAsync(await RequireAsync(storeId, id, ct), ct);
    public async Task<InputInvoiceXmlPreviewDto> PreviewAsync(int storeId, int id, CancellationToken ct)
    {
        var invoice = parser.Parse(await XmlAsync(storeId, id, ct));
        return new InputInvoiceXmlPreviewDto { InvoiceTemplateCode = invoice.InvoiceTemplateCode, InvoiceSeries = invoice.InvoiceSeries,
            InvoiceNumber = invoice.InvoiceNumber, InvoiceDate = invoice.InvoiceDate, TaxAuthorityCode = invoice.TaxAuthorityCode,
            SellerName = invoice.SellerName, SellerTaxCode = invoice.SellerTaxCode, SellerAddress = invoice.SellerAddress,
            BuyerName = invoice.BuyerName, BuyerTaxCode = invoice.BuyerTaxCode, BuyerAddress = invoice.BuyerAddress,
            BuyerOwnerMatchesReceipt = true, BuyerOwnerResolutionStatus = "Resolved", ResolvedBuyerLegalEntityName = invoice.BuyerName,
            TotalBeforeTax = invoice.TotalBeforeTax, TotalTaxAmount = invoice.TotalTaxAmount,
            TotalPaymentAmount = invoice.TotalPaymentAmount, Lines = invoice.Details.OrderBy(x => x.LineNo).Select(x => new InputInvoiceXmlPreviewLineDto {
                LineNo = x.LineNo, SupplierItemCode = x.SupplierItemCode, ItemName = x.ItemName, UnitName = x.UnitName,
                Quantity = x.Quantity, UnitPrice = x.UnitPrice, LineAmount = x.LineAmount, VatRate = x.VatRate, VatAmount = x.VatAmount }).ToList() };
    }
    private async Task<byte[]> ReadVerifiedXmlAsync(InputInvoiceLibraryEntry e, CancellationToken ct)
    {
        var bytes = await library.GetXmlBytesAsync(e.SourceSupplierTaxCode ?? e.SellerTaxCode, e.DocumentKey, ct);
        if (Hash(bytes) != e.XmlHash) throw new BusinessRuleException("File XML đã thay đổi sau khi nhập thư viện. Cần kiểm tra lại nguồn; bản xác nhận cũ không được áp dụng cho file mới.");
        return bytes;
    }
    private async Task<InputInvoiceLibraryEntry> RequireAsync(int storeId, int id, CancellationToken ct) =>
        await Scoped(storeId).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy hóa đơn trong cửa hàng hiện tại.");
    private async Task<HashSet<string>> AllowedBuyersAsync(int storeId, CancellationToken ct) =>
        (await db.LegalEntities.Where(x => x.StoreId == storeId && x.IsActive && x.NormalizedTaxCode != null)
            .GroupBy(x => x.NormalizedTaxCode!).Where(g => g.Count() == 1).Select(g => g.Key).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string Identity(InputInvoiceHead invoice) => Hash(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new[] {
        invoice.NormalizedSellerTaxCode, invoice.NormalizedInvoiceSeries, invoice.NormalizedInvoiceNumber,
        invoice.InvoiceIdentityDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) }));
}
