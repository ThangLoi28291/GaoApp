using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.DTOs.Products.BarcodeVerification;
using GaoApp.Application.Interfaces.Services.Products;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Products;

/// <summary>Receipt-local aliases become catalog barcodes only after an explicit review.</summary>
public sealed class ReceiptBarcodeProposalService(
    AppDbContext db, ITenantContext tenant, IBarcodeLookupService barcodes) : IReceiptBarcodeProposalService
{
    public async Task<ReceiptBarcodeContext> ContextAsync(int storeId, int documentId, CancellationToken ct)
    {
        var document = await DocumentAsync(storeId, documentId, false, ct);
        return new(document.ReceiptSource, document.Status);
    }

    public async Task<List<BarcodeVerificationManagementDto>> ListAsync(int storeId, int documentId, CancellationToken ct)
    {
        var document = await DocumentAsync(storeId, documentId, false, ct);
        var items = await db.ProductBarcodeVerificationRequests.AsNoTracking()
            .Where(x => x.StoreId == storeId && x.StockDocumentId == documentId && !x.IsDeleted)
            .OrderBy(x => x.Status).ThenByDescending(x => x.RequestedAtUtc)
            .Select(x => new BarcodeVerificationManagementDto
            {
                Id = x.Id, ProductVariantId = x.ProductVariantId, ProductUnitConversionId = x.ProductUnitConversionId,
                StockDocumentId = documentId, ProductNameSnapshot = x.ProductNameSnapshot,
                UnitNameSnapshot = x.UnitNameSnapshot, FactorSnapshot = x.FactorSnapshot,
                SuggestedBarcode = x.SuggestedBarcode, RequestType = x.RequestType, Status = x.Status,
                EmployeeNote = x.EmployeeNote, ManagerNote = x.ManagerNote,
                RequestedByUserId = x.RequestedByUserId, RequestedAtUtc = x.RequestedAtUtc,
                RequestedByUserName = db.Users.Where(u => u.Id == x.RequestedByUserId).Select(u => u.FullName ?? u.UserName).FirstOrDefault(),
                ResolvedByUserId = x.ResolvedByUserId, ResolvedAtUtc = x.ResolvedAtUtc,
                ResolvedByUserName = db.Users.Where(u => u.Id == x.ResolvedByUserId).Select(u => u.FullName ?? u.UserName).FirstOrDefault(),
                CreatedBarcodeId = x.CreatedBarcodeId, StockDocumentNo = document.DocumentNo
            }).ToListAsync(ct);
        foreach (var item in items)
        {
            // SQL datetime2 does not preserve DateTime.Kind. Emit UTC so browser history
            // shows the operator's local time rather than interpreting UTC as local time.
            item.RequestedAtUtc = DateTime.SpecifyKind(item.RequestedAtUtc, DateTimeKind.Utc);
            if (item.ResolvedAtUtc.HasValue)
                item.ResolvedAtUtc = DateTime.SpecifyKind(item.ResolvedAtUtc.Value, DateTimeKind.Utc);
            item.StatusText = item.Status switch
            {
                BarcodeVerificationRequestStatus.Pending => "Mã mới – chờ duyệt",
                BarcodeVerificationRequestStatus.Approved => "Đã duyệt thêm mã",
                BarcodeVerificationRequestStatus.Rejected => "Đã từ chối mã",
                _ => "Đã xác nhận không có mã"
            };
        }
        return items;
    }

    public async Task<List<StockDocumentLookupSelect2ItemDto>> SearchAsync(
        int storeId, int documentId, string term, bool catalogOnly, CancellationToken ct)
    {
        await DocumentAsync(storeId, documentId, false, ct);
        term = term.Trim();
        if (term.Length == 0) return [];
        if (term.Length > 100) throw new BusinessRuleException("Từ khóa quá dài.");
        // Check the live catalog first: a pending proposal must never mask a conflicting assignment.
        // Manufacturer codes may be alphanumeric (Code 128), not just long digit strings.
        var exactConversion = await db.ProductVariantUnitBarcodes.AsNoTracking().Where(x =>
            x.StoreId == storeId && !x.IsDeleted && x.IsActive && x.Barcode == term)
            .Select(x => (int?)x.ProductUnitConversionId).SingleOrDefaultAsync(ct);
        var global = exactConversion.HasValue
            ? new List<StockDocumentLookupSelect2ItemDto> { Lookup(await ConversionAsync(storeId, exactConversion.Value, ct), term, "UnitBarcode") }
            : await barcodes.SearchForStockDocumentSelect2Async(term, 30, ct);
        if (catalogOnly) return global;
        var pending = await db.ProductBarcodeVerificationRequests.AsNoTracking().Where(x =>
            x.StoreId == storeId && x.StockDocumentId == documentId && !x.IsDeleted &&
            x.Status == BarcodeVerificationRequestStatus.Pending && x.SuggestedBarcode == term).ToListAsync(ct);
        if (pending.Count == 0) return global;
        if (pending.Select(x => x.ProductUnitConversionId).Distinct().Count() != 1)
            throw new BusinessRuleException("Mã đang được đề xuất cho nhiều đơn vị. Quản lý cần kiểm tra lại.");
        var proposal = pending[0];
        var conversion = await ConversionAsync(storeId, proposal.ProductUnitConversionId, ct);
        EnsureSnapshot(proposal, conversion);
        await EnsureAvailableAsync(storeId, term, conversion.Id, ct);
        return [Lookup(conversion, term, "Mã mới – chờ duyệt")];
    }

    public async Task<List<StockDocumentLookupSelect2ItemDto>> UnitsAsync(int storeId, int documentId, int variantId, CancellationToken ct)
    {
        await DocumentAsync(storeId, documentId, false, ct);
        // Load the complete unit set after choosing a product. Autocomplete may have
        // been limited mid-product, or may have matched only one exact barcode.
        var units = await ActiveConversions(db.ProductUnitConversions, storeId)
            .Where(x => x.ProductVariantId == variantId)
            .OrderByDescending(x => x.IsBaseUnit).ThenBy(x => x.Factor).ThenBy(x => x.SortOrder).ThenBy(x => x.Id)
            .ToListAsync(ct);
        return units.Select(x => Lookup(x, x.Barcodes
            .Where(b => b.StoreId == storeId && b.IsActive && !b.IsDeleted)
            .OrderByDescending(b => b.IsPrimary).ThenBy(b => b.Id).FirstOrDefault()?.Barcode ?? "", "UnitBarcode")).ToList();
    }

    public async Task<StockDocumentLookupSelect2ItemDto> ProposeAsync(
        int storeId, int documentId, int userId, ProposeReceiptBarcodeRequest input, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var item = await ProposeWithinTransactionAsync(storeId, documentId, userId, input, ct);
        await transaction.CommitAsync(ct);
        return item;
    }

    public async Task<StockDocumentLookupSelect2ItemDto> ProposeWithinTransactionAsync(
        int storeId, int documentId, int userId, ProposeReceiptBarcodeRequest input, CancellationToken ct)
    {
        RequireStore(storeId);
        if (userId <= 0) throw new BusinessRuleException("Không xác định được nhân viên.");
        var code = ValidateBarcode(input.Barcode);
        var note = ValidateNote(input.Note);
        await LockAsync(storeId, ct);
        var document = await DocumentAsync(storeId, documentId, true, ct);
        if (document.Status is not (StockDocumentStatus.Draft or StockDocumentStatus.Rejected))
            throw new BusinessRuleException("Chỉ đề xuất mã khi phiếu đang nhập hoặc được trả về sửa.");
        if (document.ReceiptSource == PurchaseReceiptSource.PurchaseOrder &&
            (document.ReceivingSessionState != ReceivingSessionState.Active ||
             document.ReceivingOwnerUserId != userId || input.LeaseToken == null ||
             document.ReceivingLeaseToken != input.LeaseToken ||
             document.ReceivingLeaseExpiresAtUtc == null || document.ReceivingLeaseExpiresAtUtc <= DateTime.UtcNow))
            throw new BusinessRuleException("Phiên nhận hàng đã hết hạn hoặc thuộc nhân viên khác. Hãy nhận quyền thao tác lại.");
        var conversion = await ConversionAsync(storeId, input.ProductUnitConversionId, ct, locked: true);
        if (conversion.Factor != input.Factor)
            throw new BusinessRuleException("Quy đổi vừa thay đổi. Hãy tìm và chọn lại đơn vị.");
        // Existing schema stores snapshots to three decimal places; never silently round a conversion.
        if (decimal.Round(conversion.Factor, 3) != conversion.Factor)
            throw new BusinessRuleException("Quy đổi này cần cập nhật độ chính xác trước khi đề xuất mã.");
        var existing = await EnsureAvailableAsync(storeId, code, conversion.Id, ct);
        if (existing != null)
            throw new BusinessRuleException("Mã này đã có trong danh mục. Hãy quét lại để nhập hàng.");
        var pending = await db.ProductBarcodeVerificationRequests.Where(x => x.StoreId == storeId &&
            !x.IsDeleted && x.SuggestedBarcode == code && x.Status == BarcodeVerificationRequestStatus.Pending).ToListAsync(ct);
        if (pending.Any(x => x.ProductUnitConversionId != conversion.Id))
            throw new BusinessRuleException("Mã đang chờ duyệt cho sản phẩm hoặc đơn vị khác. Hãy nhờ quản lý kiểm tra.");
        var same = pending.FirstOrDefault(x => x.StockDocumentId == documentId);
        if (same != null) EnsureSnapshot(same, conversion);
        else db.ProductBarcodeVerificationRequests.Add(new ProductBarcodeVerificationRequest
        {
            StoreId = storeId, StockDocumentId = documentId, ProductVariantId = conversion.ProductVariantId,
            ProductUnitConversionId = conversion.Id,
            ProductNameSnapshot = conversion.ProductVariant.ProductVariantName ?? conversion.ProductVariant.Product.Name,
            UnitNameSnapshot = conversion.Unit.Name, FactorSnapshot = conversion.Factor,
            SuggestedBarcode = code, RequestType = BarcodeVerificationRequestType.SupplierBarcode,
            Status = BarcodeVerificationRequestStatus.Pending, EmployeeNote = note,
            RequestedByUserId = userId, RequestedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        return Lookup(conversion, code, "Mã mới – chờ duyệt");
    }

    public async Task ResolveAsync(int storeId, int? documentId, int requestId, int userId,
        BarcodeVerificationRequestStatus decision, string? note, CancellationToken ct)
    {
        RequireStore(storeId);
        if (userId <= 0) throw new BusinessRuleException("Không xác định được người duyệt.");
        if (decision is not (BarcodeVerificationRequestStatus.Approved or BarcodeVerificationRequestStatus.Rejected or BarcodeVerificationRequestStatus.ConfirmedNoBarcode))
            throw new BusinessRuleException("Thao tác duyệt không hợp lệ.");
        note = ValidateNote(note);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(storeId, ct);
        var request = await db.ProductBarcodeVerificationRequests.SingleOrDefaultAsync(x => x.Id == requestId &&
            x.StoreId == storeId && !x.IsDeleted && (!documentId.HasValue || x.StockDocumentId == documentId), ct)
            ?? throw new BusinessRuleException("Không tìm thấy đề xuất mã trong phiếu này.");
        if (request.Status == decision) { await transaction.CommitAsync(ct); return; }
        if (request.Status != BarcodeVerificationRequestStatus.Pending)
            throw new BusinessRuleException("Đề xuất đã được xử lý. Hãy tải lại phiếu.");
        if (request.StockDocumentId is not int receiptId)
            throw new BusinessRuleException("Đề xuất không có phiếu nhập để kiểm tra.");
        var document = await DocumentAsync(storeId, receiptId, true, ct);
        // Receipt approvers review received units. The existing catalog-normalization screen
        // has its own Catalog.Barcode.Update authority and can normalize other units earlier.
        if (documentId.HasValue && document.Status is not (StockDocumentStatus.PendingApproval or StockDocumentStatus.Confirmed))
            throw new BusinessRuleException("Duyệt hoặc từ chối mã khi phiếu chờ duyệt hoặc đã duyệt.");
        if (decision == BarcodeVerificationRequestStatus.Approved)
        {
            if (request.RequestType != BarcodeVerificationRequestType.SupplierBarcode)
                throw new BusinessRuleException("Đề xuất này không có mã để thêm.");
            var code = ValidateBarcode(request.SuggestedBarcode);
            var conversion = await ConversionAsync(storeId, request.ProductUnitConversionId, ct, locked: true);
            EnsureSnapshot(request, conversion);
            var received = await db.StockDocumentLines.AnyAsync(x => x.StockDocumentId == receiptId && !x.IsDeleted &&
                x.Quantity > 0 && x.ProductVariantId == conversion.ProductVariantId && x.UnitId == conversion.UnitId &&
                x.Factor == conversion.Factor && (x.ProductUnitConversionId == null || x.ProductUnitConversionId == conversion.Id), ct);
            if (documentId.HasValue && !received) throw new BusinessRuleException("Phiếu chưa có hàng nhập theo đúng đơn vị và quy đổi của mã này. Có thể từ chối mã hoặc trả phiếu về sửa.");
            var barcode = await EnsureAvailableAsync(storeId, code, conversion.Id, ct);
            if (barcode == null)
            {
                barcode = new ProductVariantUnitBarcode
                {
                    StoreId = storeId, ProductUnitConversionId = conversion.Id, Barcode = code,
                    BarcodeType = BarcodeType.Supplier, IsPrimary = false, IsActive = true,
                    Note = $"Duyệt từ phiếu {document.DocumentNo}, đề xuất #{request.Id}",
                    CreatedBy = userId, UpdatedBy = userId, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
                };
                db.ProductVariantUnitBarcodes.Add(barcode);
                try { await db.SaveChangesAsync(ct); }
                catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
                { throw new BusinessRuleException("Mã vừa được xử lý bởi thao tác khác. Hãy tải lại và kiểm tra mã."); }
                db.ProductVariantBarcodeHistories.Add(new ProductVariantBarcodeHistory
                {
                    StoreId = storeId, ProductVariantId = conversion.ProductVariantId, ProductUnitConversionId = conversion.Id,
                    NewBarcodeId = barcode.Id, NewBarcode = code, ActionType = BarcodeHistoryActionType.Assigned,
                    Reason = $"Thêm mã hãng từ phiếu {document.DocumentNo}, đề xuất #{request.Id}",
                    ChangedByUserId = userId, ChangedAtUtc = DateTime.UtcNow, CreatedBy = userId
                });
            }
            request.CreatedBarcodeId = barcode.Id;
        }
        else if (decision == BarcodeVerificationRequestStatus.ConfirmedNoBarcode && request.RequestType != BarcodeVerificationRequestType.NoSupplierBarcode)
            throw new BusinessRuleException("Đề xuất này có mã; hãy duyệt hoặc từ chối mã.");
        request.Status = decision;
        request.ManagerNote = note;
        request.ResolvedByUserId = userId;
        request.ResolvedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private void RequireStore(int storeId)
    {
        if (storeId <= 0 || tenant.StoreId != storeId)
            throw new BusinessRuleException("Không xác định được cửa hàng hiện tại.");
    }

    private async Task LockAsync(int storeId, CancellationToken ct)
    {
        var resource = $"receipt-barcode-proposals:{storeId}";
        await db.Database.ExecuteSqlInterpolatedAsync($@"DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
            IF @result < 0 THROW 51000, 'Barcode review is busy. Please retry.', 1;", ct);
    }

    private async Task<StockDocument> DocumentAsync(int storeId, int id, bool locked, CancellationToken ct)
    {
        RequireStore(storeId);
        var query = locked ? db.StockDocuments.FromSqlInterpolated($"SELECT * FROM [StockDocument] WITH (UPDLOCK,HOLDLOCK,ROWLOCK) WHERE [Id]={id} AND [StoreId]={storeId}")
            : db.StockDocuments.AsNoTracking();
        var document = await query.Include(x => x.Warehouse).ThenInclude(x => x.LegalEntity)
            .Include(x => x.Supplier).Include(x => x.PurchaseOrder)
            .SingleOrDefaultAsync(x => x.Id == id && x.StoreId == storeId && !x.IsDeleted && x.Type == StockDocumentType.Receipt, ct)
            ?? throw new BusinessRuleException("Không tìm thấy phiếu nhập của cửa hàng.");
        var warehouse = document.Warehouse;
        if (warehouse == null || warehouse.IsDeleted || !warehouse.IsActive || warehouse.StoreId != storeId ||
            warehouse.LegalEntity == null || warehouse.LegalEntity.IsDeleted || !warehouse.LegalEntity.IsActive || warehouse.LegalEntity.StoreId != storeId)
            throw new BusinessRuleException("Kho hoặc chủ thể pháp lý của phiếu không hợp lệ.");
        if (document.SupplierId != null && (document.Supplier == null || document.Supplier.IsDeleted || !document.Supplier.IsActive || document.Supplier.StoreId != storeId))
            throw new BusinessRuleException("Nhà cung cấp của phiếu không hợp lệ.");
        if (document.ReceiptSource == PurchaseReceiptSource.PurchaseOrder &&
            (document.PurchaseOrder == null || document.PurchaseOrder.IsDeleted || document.PurchaseOrder.StoreId != storeId ||
             document.PurchaseOrder.SupplierId != document.SupplierId || document.PurchaseOrder.ExpectedWarehouseId != document.WarehouseId))
            throw new BusinessRuleException("Liên kết PO và kho nhận không hợp lệ.");
        return document;
    }

    private async Task<ProductUnitConversion> ConversionAsync(int storeId, int id, CancellationToken ct, bool locked = false)
    {
        var query = locked ? db.ProductUnitConversions.FromSqlInterpolated($"SELECT * FROM [ProductUnitConversion] WITH (UPDLOCK,HOLDLOCK,ROWLOCK) WHERE [Id]={id} AND [StoreId]={storeId}")
            : db.ProductUnitConversions.AsQueryable();
        return await ActiveConversions(query, storeId).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new BusinessRuleException("Sản phẩm hoặc đơn vị không còn sử dụng được.");
    }

    private static IQueryable<ProductUnitConversion> ActiveConversions(IQueryable<ProductUnitConversion> query, int storeId)
        => query.AsNoTracking().Include(x => x.Unit).Include(x => x.Barcodes)
            .Include(x => x.ProductVariant).ThenInclude(x => x.Product).ThenInclude(x => x.BaseUnit)
            .Include(x => x.ProductVariant).ThenInclude(x => x.PrimaryProductImage).ThenInclude(x => x!.MediaAsset)
            .Where(x => x.StoreId == storeId && !x.IsDeleted && x.IsActive && x.Factor > 0 &&
                !x.Unit.IsDeleted && x.Unit.IsActive && x.Unit.StoreId == storeId &&
                x.ProductVariant.StoreId == storeId && !x.ProductVariant.IsDeleted && x.ProductVariant.IsActive &&
                x.ProductVariant.Product.StoreId == storeId && !x.ProductVariant.Product.IsDeleted && x.ProductVariant.Product.IsActive);

    private static void EnsureSnapshot(ProductBarcodeVerificationRequest request, ProductUnitConversion conversion)
    {
        if (request.ProductVariantId != conversion.ProductVariantId || request.FactorSnapshot != conversion.Factor || request.UnitNameSnapshot != conversion.Unit.Name)
            throw new BusinessRuleException("Đơn vị hoặc quy đổi đã thay đổi sau khi đề xuất. Hãy từ chối mã và nhập lại đúng đơn vị.");
    }

    private async Task<ProductVariantUnitBarcode?> EnsureAvailableAsync(int storeId, string code, int conversionId, CancellationToken ct)
    {
        var records = await db.ProductVariantUnitBarcodes.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.StoreId == storeId && x.Barcode == code).ToListAsync(ct);
        if (records.Any(x => x.ProductUnitConversionId != conversionId))
            throw new BusinessRuleException("Mã đã thuộc sản phẩm hoặc đơn vị khác; không thể gắn lại.");
        var active = records.FirstOrDefault(x => x.IsActive && !x.IsDeleted);
        if (records.Count > 0 && active == null)
            throw new BusinessRuleException("Mã đã ngừng sử dụng. Quản lý cần kiểm tra lịch sử barcode.");
        if (await db.ProductVariantBarcodeHistories.IgnoreQueryFilters().AnyAsync(x => x.StoreId == storeId &&
            (x.OldBarcode == code || x.NewBarcode == code) && (x.ProductUnitConversionId != conversionId || active == null), ct))
            throw new BusinessRuleException("Mã đã có trong lịch sử; cần kiểm tra trước khi sử dụng lại.");
        return active;
    }

    private static string ValidateBarcode(string? value)
    {
        var code = value?.Trim() ?? "";
        if (code.Length is < 1 or > 64 || code.Any(c => c < 33 || c > 126))
            throw new BusinessRuleException("Mã vạch cần 1–64 ký tự ASCII, không chứa khoảng trắng. Giữ nguyên số 0 ở đầu mã.");
        return code;
    }

    private static string? ValidateNote(string? note)
    {
        note = note?.Trim();
        if (note?.Length > 1000) throw new BusinessRuleException("Ghi chú tối đa 1.000 ký tự.");
        return note;
    }

    private static StockDocumentLookupSelect2ItemDto Lookup(ProductUnitConversion conversion, string code, string source)
        => new()
        {
            ProductVariantId = conversion.ProductVariantId, ProductUnitConversionId = conversion.Id, UnitId = conversion.UnitId,
            ProductName = conversion.ProductVariant.ProductVariantName ?? conversion.ProductVariant.Product.Name,
            Sku = conversion.ProductVariant.Sku, Barcode = code, UnitName = conversion.Unit.Name,
            BaseUnitName = conversion.ProductVariant.Product.BaseUnit.Name, IsBaseUnit = conversion.IsBaseUnit,
            Factor = conversion.Factor, Price = conversion.Price ?? conversion.ProductVariant.Price ?? conversion.ProductVariant.Product.BasePrice, SourceType = source,
            ImageUrl = ImageUrl(conversion.ProductVariant),
            Text = $"{conversion.ProductVariant.ProductVariantName ?? conversion.ProductVariant.Product.Name} · {conversion.Unit.Name} (×{conversion.Factor}) · {code} · {source}"
        };

    private static string? ImageUrl(ProductVariant variant)
    {
        var path = variant.PrimaryProductImage?.MediaAsset?.StoragePath?.Trim();
        if (string.IsNullOrEmpty(path)) return null;
        return path.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            ? path : "/" + path.TrimStart('/');
    }
}
