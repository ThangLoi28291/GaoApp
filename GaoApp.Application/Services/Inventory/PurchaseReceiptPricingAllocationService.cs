using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Services.Purchases;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Inventory;

public sealed class PurchaseReceiptPricingAllocationService(
    IPurchaseReceiptPricingPlanRepository plans, IStockDocumentRepository receipts,
    PurchaseReceiptPricingAllocationCalculator calculator, ITenantContext tenant,
    IInputInvoiceReconciliationService? reconciliation = null) : IPurchaseReceiptPricingAllocationService
{
    private readonly Dictionary<int, PurchaseReceiptPricingPlan> _confirmPlans = [];
    private int StoreId => tenant.StoreId is > 0 ? tenant.StoreId.Value : throw new BusinessRuleException("Cần cửa hàng hiện tại để quản lý giá nhập.");
    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    public async Task<PurchaseReceiptPricingAllocationWorkspace> GetAsync(int receiptId, CancellationToken ct = default)
    {
        var receipt = await LoadReceipt(receiptId, false, ct);
        var plan = await plans.GetPlanAsync(StoreId, receiptId, false, ct);
        var context = plan?.State == PurchaseReceiptPricingPlanState.Confirmed
            ? ConfirmedPhysicalSnapshot(plan, receipt) : await PhysicalLines(receipt, ct);
        var draft = plan is null ? DefaultDraft(receipt, context) : ToRequest(plan);
        return new()
        {
            PlanId = plan?.Id, State = plan?.State, ReceiptRowVersion = Version(receipt.RowVersion), PlanRowVersion = plan is null ? null : Version(plan.RowVersion),
            CanEdit = receipt.Status == StockDocumentStatus.PendingApproval,
            IsStale = plan is not null && plan.State != PurchaseReceiptPricingPlanState.Confirmed && !Fresh(plan, receipt, context),
            PhysicalLines = context, Draft = draft,
            Preview = plan is null ? null : calculator.Calculate(draft, context, Values(plan))
        };
    }

    public async Task<PurchaseReceiptPricingAllocationPreview> PreviewAsync(int receiptId, PurchaseReceiptPricingAllocationRequest request, CancellationToken ct = default)
    {
        var receipt = await LoadReceipt(receiptId, false, ct);
        EnsureEditable(receipt); ValidatePrecision(request); EnsureVersion(receipt.RowVersion, request.ReceiptRowVersion);
        var context = await PhysicalLines(receipt, ct);
        EnsureLineVersions(request, context);
        return calculator.Calculate(request, context, await ResolveGiftValues(request, context, false, ct));
    }

    public async Task<PurchaseReceiptPricingAllocationWorkspace> SaveAsync(int receiptId, PurchaseReceiptPricingAllocationRequest request, CancellationToken ct = default)
    {
        await receipts.BeginTransactionAsync(ct);
        try
        {
            var receipt = await LoadReceipt(receiptId, true, ct);
            EnsureEditable(receipt); ValidatePrecision(request); EnsureVersion(receipt.RowVersion, request.ReceiptRowVersion);
            var context = await PhysicalLines(receipt, ct);
            EnsureLineVersions(request, context);
            var plan = await plans.GetPlanAsync(StoreId, receiptId, true, ct);
            if (plan is null)
            {
                if (!string.IsNullOrWhiteSpace(request.PlanRowVersion)) throw Conflict();
                plan = new() { StoreId = StoreId, StockDocumentId = receiptId };
                plans.Add(plan);
            }
            else
            {
                if (plan.State == PurchaseReceiptPricingPlanState.Confirmed) throw Conflict("Kế hoạch đã xác nhận chỉ được xem bằng chứng.");
                EnsureVersion(plan.RowVersion, request.PlanRowVersion);
            }
            var preview = calculator.Calculate(request, context, await ResolveGiftValues(request, context, true, ct));
            RequireValid(preview, allowTotalMismatch: true);
            ReplaceGraph(plan, request, context, preview);
            plan.State = PurchaseReceiptPricingPlanState.Draft;
            plan.AppliedAtUtc = null;
            plan.ActualBillTotal = request.ActualBillTotal;
            plan.GlobalDiscountPercent = request.GlobalDiscountPercent;
            plan.SystemTotal = preview.SystemTotal;
            plan.ReceiptRowVersionSnapshot = receipt.RowVersion.ToArray();
            plan.PhysicalDependencyHash = DependencyHash(receipt, context);
            plan.UpdatedAtUtc = DateTime.UtcNow;
            await receipts.SaveChangesAsync(ct);
            await receipts.CommitTransactionAsync(ct);
        }
        catch { await receipts.RollbackTransactionAsync(ct); throw; }
        return await GetAsync(receiptId, ct);
    }

    public async Task<PurchaseReceiptPricingAllocationWorkspace> ApplyAsync(int receiptId, PurchaseReceiptPricingApplyRequest request, CancellationToken ct = default)
    {
        await receipts.BeginTransactionAsync(ct);
        try
        {
            var snapshot = await LoadReceipt(receiptId, true, ct);
            EnsureEditable(snapshot); EnsureVersion(snapshot.RowVersion, request.ReceiptRowVersion);
            var plan = await plans.GetPlanAsync(StoreId, receiptId, true, ct) ?? throw new BusinessRuleException("Lưu kế hoạch giá trước khi Apply.");
            EnsureVersion(plan.RowVersion, request.PlanRowVersion);
            var context = await PhysicalLinesForSavedPlan(snapshot, ct);
            if (!Fresh(plan, snapshot, context)) throw Conflict("Dữ liệu thực nhận hoặc quy đổi đã thay đổi. Tính lại và lưu kế hoạch trước khi Apply.");
            var preview = calculator.Calculate(ToRequest(plan), context, Values(plan));
            RequireValid(preview, false);
            EnsureCalculatedEvidence(plan, preview);
            var receipt = await receipts.GetForConfirmAsync(receiptId, ct) ?? throw new BusinessRuleException("Không tìm thấy phiếu nhập.");
            if (receipt.StoreId != StoreId) throw new BusinessRuleException("Không tìm thấy phiếu nhập.");
            EnsureVersion(receipt.RowVersion, request.ReceiptRowVersion);
            var financial = preview.Lines.ToDictionary(x => x.StockDocumentLineId);
            foreach (var line in receipt.Lines.Where(x => !x.IsDeleted))
            {
                var amount = financial[line.Id].FinalAmountBeforeVat;
                line.UnitPriceBeforeVat = Money(amount / line.Quantity);
                line.VatAmount = receipt.HasVat ? Money(checked(amount * line.TaxRate / 100m)) : 0m;
                line.LineTotal = checked(amount + line.VatAmount);
                line.UnitPriceAfterVat = Money(line.LineTotal / line.Quantity);
                line.UnitCost = line.UnitPriceAfterVat;
            }
            receipt.SubtotalBeforeVat = preview.SystemTotal;
            receipt.VatAmount = receipt.Lines.Where(x => !x.IsDeleted).Sum(x => x.VatAmount);
            receipt.TotalAmount = checked(receipt.SubtotalBeforeVat + receipt.VatAmount);
            receipt.UpdatedAtUtc = DateTime.UtcNow;
            if (reconciliation is not null)
                await reconciliation.InvalidateWithinTransactionAsync(StoreId, receiptId, "Kế hoạch giá và khuyến mãi đã Apply; cần đối chiếu lại trước khi duyệt.", ct);
            plan.State = PurchaseReceiptPricingPlanState.Applied;
            plan.AppliedAtUtc = DateTime.UtcNow;
            plan.UpdatedAtUtc = DateTime.UtcNow;
            await receipts.SaveChangesAsync(ct);
            // Apply changes the receipt/line concurrency tokens. Bind the plan to those new tokens in this same transaction.
            var applied = await LoadReceipt(receiptId, true, ct);
            var appliedContext = await PhysicalLines(applied, ct);
            plan.ReceiptRowVersionSnapshot = applied.RowVersion.ToArray();
            plan.PhysicalDependencyHash = DependencyHash(applied, appliedContext);
            var appliedLines = appliedContext.ToDictionary(x => x.StockDocumentLineId);
            foreach (var line in plan.Lines.Where(x => !x.IsDeleted))
                line.ReceiptLineRowVersionSnapshot = Convert.FromBase64String(appliedLines[line.StockDocumentLineId].RowVersion);
            await receipts.SaveChangesAsync(ct);
            await receipts.CommitTransactionAsync(ct);
        }
        catch { await receipts.RollbackTransactionAsync(ct); throw; }
        return await GetAsync(receiptId, ct);
    }

    public async Task<bool> HasAppliedPlanAsync(int receiptId, CancellationToken ct = default)
        => (await plans.GetPlanAsync(StoreId, receiptId, false, ct))?.State == PurchaseReceiptPricingPlanState.Applied;

    public async Task<IReadOnlyDictionary<int, decimal>?> GetSavedGoodsAmountsForConfirmAsync(StockDocument receipt, bool withinConfirmTransaction, CancellationToken ct = default)
    {
        if (receipt.StoreId != StoreId) throw new BusinessRuleException("Không tìm thấy phiếu nhập.");
        // Use receipt -> plan lock order consistently with Save/Apply, including the
        // absence of a plan, so a concurrent draft cannot appear during legacy posting.
        var lockedSnapshot = withinConfirmTransaction ? await LoadReceipt(receipt.Id, true, ct) : null;
        if (lockedSnapshot is not null) EnsureVersion(lockedSnapshot.RowVersion, Version(receipt.RowVersion));
        var plan = await plans.GetPlanAsync(StoreId, receipt.Id, withinConfirmTransaction, ct);
        if (plan is null) return null;
        var snapshot = lockedSnapshot ?? await LoadReceipt(receipt.Id, false, ct);
        // The plan is a calculation helper. Confirmation uses the saved Goods prices,
        // including manual corrections, without recalculating or reapplying the plan.
        // Keep exact amounts on untouched allocated lines despite rounded display prices.
        var savedLines = snapshot.Lines.Where(x => !x.IsDeleted).ToArray();
        // Unsaved suggested prices follow the existing commercial approval path.
        // Merely saving a helper draft must not make that path unavailable.
        var pricedLines = savedLines.Where(x => x.UnitPriceBeforeVat > 0).ToArray();
        if (pricedLines.Any(x => x.Quantity <= 0 ||
            x.LineTotal - x.VatAmount <= 0 ||
            (Money(x.Quantity * x.UnitPriceBeforeVat) != x.LineTotal - x.VatAmount &&
             Money((x.LineTotal - x.VatAmount) / x.Quantity) != x.UnitPriceBeforeVat)))
            throw Conflict("Giá hoặc thành tiền trên tab Hàng hóa chưa hợp lệ. Kiểm tra và lưu giá nhập trước khi duyệt.");
        var values = pricedLines.ToDictionary(x => x.Id, x => x.LineTotal - x.VatAmount);
        var versions = savedLines.ToDictionary(x => x.Id, x => x.RowVersion);
        var active = receipt.Lines.Where(x => !x.IsDeleted).ToArray();
        if (!versions.Keys.ToHashSet().SetEquals(active.Select(x => x.Id)) ||
            active.Any(x => (values.TryGetValue(x.Id, out var amount) && x.LineTotal - x.VatAmount != amount) ||
                (withinConfirmTransaction && !x.RowVersion.SequenceEqual(versions[x.Id]))))
            throw Conflict("Giá trên tab Hàng hóa đã thay đổi. Tải lại phiếu trước khi duyệt.");
        if (withinConfirmTransaction) _confirmPlans[receipt.Id] = plan;
        return values.Count == 0 ? null : values;
    }

    public Task MarkConfirmedWithinTransactionAsync(StockDocument receipt, CancellationToken ct = default)
    {
        if (_confirmPlans.TryGetValue(receipt.Id, out var plan))
        {
            if (plan.StoreId != StoreId || plan.StockDocumentId != receipt.Id ||
                plan.State is not (PurchaseReceiptPricingPlanState.Draft or PurchaseReceiptPricingPlanState.Applied))
                throw Conflict();
            plan.State = PurchaseReceiptPricingPlanState.Confirmed;
            plan.ConfirmedAtUtc = DateTime.UtcNow;
        }
        return Task.CompletedTask;
    }

    private async Task<StockDocument> LoadReceipt(int id, bool forUpdate, CancellationToken ct)
    {
        var receipt = await plans.GetReceiptSnapshotAsync(StoreId, id, forUpdate, ct);
        if (receipt is null || receipt.Type != StockDocumentType.Receipt || receipt.StoreId != StoreId)
            throw new BusinessRuleException("Không tìm thấy phiếu nhập.");
        return receipt;
    }

    private async Task<List<PurchaseReceiptPricingPhysicalLine>> PhysicalLines(StockDocument receipt, CancellationToken ct)
    {
        var active = receipt.Lines.Where(x => !x.IsDeleted).OrderBy(x => x.LineNo).ThenBy(x => x.Id).ToArray();
        var conversions = await plans.GetConversionsAsync(StoreId, active.Select(x => x.ProductVariantId).Distinct().ToArray(), ct);
        var output = new List<PurchaseReceiptPricingPhysicalLine>();
        foreach (var line in active)
        {
            var variant = line.ProductVariant;
            if (variant is null || variant.IsDeleted || variant.StoreId != StoreId || variant.Product is null || variant.Product.IsDeleted || variant.Product.StoreId != StoreId)
                throw new BusinessRuleException("Danh mục của dòng thực nhận không còn hợp lệ.");
            var units = conversions.Where(x => x.ProductVariantId == line.ProductVariantId)
                .Select(x => new PurchaseReceiptPricingUnit(x.UnitId, x.Unit.Name, x.Id, x.Factor, Version(x.RowVersion), Version(x.Unit.RowVersion))).ToList();
            var physicalUnit = line.UnitId ?? variant.Product.BaseUnitId;
            if (!units.Any(x => x.UnitId == physicalUnit && x.Factor == line.Factor &&
                (!line.ProductUnitConversionId.HasValue || x.ConversionId == line.ProductUnitConversionId.Value)))
                throw new BusinessRuleException($"Dòng {line.LineNo}: quy đổi đơn vị thực nhận không còn hợp lệ.");
            output.Add(new()
            {
                StockDocumentLineId = line.Id, LineNo = line.LineNo, ProductVariantId = line.ProductVariantId, ProductId = variant.ProductId,
                ProductName = line.ProductNameSnapshot, UnitId = physicalUnit, ProductUnitConversionId = line.ProductUnitConversionId,
                ProductImageUrl = ProductImageUrl(variant),
                Factor = line.Factor, Quantity = line.Quantity, BaseQuantity = line.BaseQuantity, RowVersion = Version(line.RowVersion),
                ProductRowVersion = Version(variant.RowVersion) + ":" + Version(variant.Product.RowVersion),
                CurrentUnitPriceBeforeVat = line.UnitPriceBeforeVat,
                CurrentAmountBeforeVat = line.LineTotal - line.VatAmount, Units = units
            });
        }
        return output;
    }

    private async Task<List<PurchaseReceiptPricingPhysicalLine>> PhysicalLinesForSavedPlan(
        StockDocument receipt, CancellationToken ct)
    {
        try { return await PhysicalLines(receipt, ct); }
        catch (BusinessRuleException)
        {
            // A previously valid dependency becoming unavailable invalidates the
            // saved plan, just like a changed row version; never apply/post it.
            throw Conflict("Dữ liệu thực nhận hoặc quy đổi đã thay đổi. Tải lại, tính lại và Apply kế hoạch trước khi duyệt.");
        }
    }

    private static List<PurchaseReceiptPricingPhysicalLine> ConfirmedPhysicalSnapshot(PurchaseReceiptPricingPlan plan, StockDocument receipt)
        => plan.Lines.Where(x => !x.IsDeleted).OrderBy(x => x.LineNo).Select(line =>
        {
            var physical = receipt.Lines.FirstOrDefault(x => x.Id == line.StockDocumentLineId);
            var units = new List<PurchaseReceiptPricingUnit>
            {
                new(line.PhysicalUnitIdSnapshot, physical?.UnitNameSnapshot ?? "Đơn vị thực nhận", line.PhysicalConversionIdSnapshot ?? 0, line.PhysicalFactorSnapshot, "", ""),
                new(line.BillUnitId, "Đơn vị bill", line.BillConversionId, line.BillFactor, "", "")
            };
            units.AddRange(plan.Rules.Where(x => !x.IsDeleted && x.GiftPlanLineId == line.Id && x.GiftUnitId.HasValue)
                .Select(x => new PurchaseReceiptPricingUnit(x.GiftUnitId!.Value, "Đơn vị quà", x.GiftConversionId ?? 0, x.GiftFactor, "", "")));
            units.AddRange(plan.GiftValuations.Where(x => !x.IsDeleted && x.ProductVariantId == line.ProductVariantIdSnapshot)
                .Select(x => new PurchaseReceiptPricingUnit(x.UnitId, "Đơn vị định giá", x.ConversionId, x.Factor, "", "")));
            units.AddRange(plan.BillLines.Where(x => !x.IsDeleted && x.ProductVariantIdSnapshot == line.ProductVariantIdSnapshot)
                .Select(x => new PurchaseReceiptPricingUnit(x.BillUnitId, x.BillUnitName, x.BillConversionId, x.BillFactor, "", "")));
            return new PurchaseReceiptPricingPhysicalLine
            {
                StockDocumentLineId = line.StockDocumentLineId, LineNo = line.LineNo, ProductVariantId = line.ProductVariantIdSnapshot, ProductId = line.ProductIdSnapshot,
                ProductName = physical?.ProductNameSnapshot ?? $"SKU {line.ProductVariantIdSnapshot}", UnitId = line.PhysicalUnitIdSnapshot,
                ProductImageUrl = ProductImageUrl(physical?.ProductVariant),
                ProductUnitConversionId = line.PhysicalConversionIdSnapshot, Factor = line.PhysicalFactorSnapshot,
                Quantity = line.PhysicalQuantitySnapshot, BaseQuantity = line.PhysicalBaseQuantitySnapshot,
                RowVersion = physical is null ? Version(line.ReceiptLineRowVersionSnapshot) : Version(physical.RowVersion),
                CurrentUnitPriceBeforeVat = physical?.UnitPriceBeforeVat ?? line.EffectiveUnitPriceBeforeVat,
                CurrentAmountBeforeVat = physical is null ? line.FinalAmountBeforeVat : physical.LineTotal - physical.VatAmount,
                Units = units.DistinctBy(x => x.UnitId).ToList()
            };
        }).ToList();

    private static string? ProductImageUrl(ProductVariant? variant)
    {
        var image = variant?.PrimaryProductImage;
        var media = image?.MediaAsset;
        if (image is null || image.IsDeleted || image.StoreId != variant!.StoreId || image.ProductId != variant.ProductId ||
            media is null || media.IsDeleted || media.StoreId != variant.StoreId || string.IsNullOrWhiteSpace(media.StoragePath))
            return null;
        var path = media.StoragePath.Trim().Replace("\\", "/");
        return path.StartsWith('/') || path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? path : "/" + path;
    }

    private async Task<List<PurchaseReceiptPricingGiftValue>> ResolveGiftValues(PurchaseReceiptPricingAllocationRequest request,
        IReadOnlyList<PurchaseReceiptPricingPhysicalLine> context, bool lockHistory, CancellationToken ct)
    {
        if (request.GiftValuations.Select(x => x.ProductVariantId).Distinct().Count() != request.GiftValuations.Count)
            throw new BusinessRuleException("Một SKU quà chỉ có một giá trị định giá trong bill.");
        var targets = request.Rules.Where(x => x.Type == PurchaseReceiptPricingRuleType.Gift && x.GiftMode == PurchaseReceiptPricingGiftMode.DifferentSku && x.GiftLineId.HasValue)
            .Select(x => new { Rule = x, Line = context.FirstOrDefault(line => line.StockDocumentLineId == x.GiftLineId) }).Where(x => x.Line is not null).ToArray();
        var variants = targets.Select(x => x.Line!.ProductVariantId).Distinct().OrderBy(x => x).ToArray();
        if (lockHistory && variants.Length > 0 && !await receipts.LockPurchasePriceHistoryVariantsAsync(StoreId, variants, ct))
            throw Conflict("Không thể khóa lịch sử giá quà. Vui lòng tải lại và thử lại.");
        var history = await plans.GetGiftHistoryAsync(StoreId, variants, ct);
        var output = new List<PurchaseReceiptPricingGiftValue>();
        foreach (var group in targets.GroupBy(x => x.Line!.ProductVariantId))
        {
            var first = group.OrderBy(x => x.Rule.GiftUnitId)
                .ThenBy(x => x.Rule.RuleKey, StringComparer.Ordinal).First();
            var input = request.GiftValuations.SingleOrDefault(x => x.ProductVariantId == group.Key);
            var unitId = input?.UnitId ?? first.Rule.GiftUnitId;
            var unit = first.Line!.Units.SingleOrDefault(x => x.UnitId == unitId);
            if (unit is null) continue;
            if (history.TryGetValue(group.Key, out var prior))
                output.Add(new(group.Key, unit.UnitId, unit.Factor, decimal.Round(checked(prior.BaseUnitValueBeforeVat * unit.Factor), 12, MidpointRounding.AwayFromZero),
                    PurchaseReceiptGiftValuationSource.LatestConfirmedPurchase, prior.ReceiptLineId, prior.ConfirmedAtUtc));
            else output.Add(new(group.Key, unit.UnitId, unit.Factor, input?.ManualUnitValueBeforeVat ?? 0m, PurchaseReceiptGiftValuationSource.Manual));
        }
        return output;
    }

    private static string Version(byte[]? value) => Convert.ToBase64String(value ?? []);
    private static PurchaseReceiptPricingConflictException Conflict(string? message = null)
        => new(message ?? "Phiếu hoặc kế hoạch giá vừa thay đổi. Vui lòng tải lại để đối chiếu trước khi lưu.");
    private static void EnsureVersion(byte[] actual, string? expected)
    {
        byte[] decoded;
        try { decoded = Convert.FromBase64String(expected ?? ""); } catch (FormatException) { throw Conflict(); }
        if (decoded.Length == 0 || !actual.SequenceEqual(decoded)) throw Conflict();
    }
    private static void EnsureEditable(StockDocument receipt)
    {
        if (receipt.Status != StockDocumentStatus.PendingApproval)
            throw new BusinessRuleException("Chỉ phiếu chờ duyệt mới được chỉnh sửa, Preview, Save hoặc Apply kế hoạch giá.");
    }
    private static void EnsureLineVersions(PurchaseReceiptPricingAllocationRequest request, IReadOnlyList<PurchaseReceiptPricingPhysicalLine> context)
    {
        var inputs = request.Lines.GroupBy(x => x.StockDocumentLineId).ToDictionary(x => x.Key, x => x.ToArray());
        if (inputs.Count != context.Count || context.Any(x => !inputs.ContainsKey(x.StockDocumentLineId) || inputs[x.StockDocumentLineId].Length != 1))
            throw Conflict("Danh sách dòng thực nhận đã thay đổi. Vui lòng tải lại.");
        foreach (var line in context) EnsureVersion(Convert.FromBase64String(line.RowVersion), inputs[line.StockDocumentLineId][0].RowVersion);
    }
    private static bool Fresh(PurchaseReceiptPricingPlan plan, StockDocument receipt, IReadOnlyList<PurchaseReceiptPricingPhysicalLine> context)
        => plan.StoreId == receipt.StoreId && plan.StockDocumentId == receipt.Id && plan.ReceiptRowVersionSnapshot.SequenceEqual(receipt.RowVersion) &&
            string.Equals(plan.PhysicalDependencyHash, DependencyHash(receipt, context), StringComparison.Ordinal);
    private static string DependencyHash(StockDocument receipt, IReadOnlyList<PurchaseReceiptPricingPhysicalLine> context)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            receipt.StoreId, receipt.Id, receipt.WarehouseId,
            Lines = context.OrderBy(x => x.StockDocumentLineId).Select(x => new { x.StockDocumentLineId, x.LineNo, x.ProductVariantId, x.ProductId,
                x.UnitId, x.ProductUnitConversionId, x.Factor, x.Quantity, x.BaseQuantity, x.RowVersion, x.ProductRowVersion,
                Units = x.Units.OrderBy(u => u.UnitId) })
        })))).ToLowerInvariant();

    private static void ValidatePrecision(PurchaseReceiptPricingAllocationRequest request)
    {
        if (request.Lines is null || request.Rules is null || request.GiftValuations is null || request.Lines.Count > 500 || request.Rules.Count > 100 || request.GiftValuations.Count > 500)
            throw new BusinessRuleException("Kế hoạch giá quá lớn hoặc không hợp lệ.");
        if (request.Lines.Any(x => x is null) || request.Rules.Any(x => x is null) || request.GiftValuations.Any(x => x is null))
            throw new BusinessRuleException("Kế hoạch giá chứa dòng hoặc quy tắc không hợp lệ.");
        if (request.BillLines is not null && (request.BillLines.Count > 500 || request.BillLines.Any(x => x is null) ||
            request.BillLines.Any(x => decimal.Round(x.BillQuantity, 9) != x.BillQuantity || decimal.Round(x.BillUnitPriceBeforeVat, 12) != x.BillUnitPriceBeforeVat) ||
            request.Rules.Any(x => x.BillSources is null || x.BillSources.Count > 500 || x.BillSources.Any(s => s is null || decimal.Round(s.Quantity, 9) != s.Quantity))))
            throw new BusinessRuleException("Danh sách bill hoặc số lượng tham gia vượt giới hạn kế hoạch.");
        if (decimal.Round(request.GlobalDiscountPercent, 4) != request.GlobalDiscountPercent ||
            request.Lines.Any(x => decimal.Round(x.BillQuantity, 9) != x.BillQuantity || decimal.Round(x.BillUnitPriceBeforeVat, 12) != x.BillUnitPriceBeforeVat) ||
            request.Rules.Any(x => decimal.Round(x.GiftQuantity, 9) != x.GiftQuantity || decimal.Round(x.DiscountPercent, 4) != x.DiscountPercent || decimal.Round(x.DiscountAmount, 2) != x.DiscountAmount) ||
            request.GiftValuations.Any(x => x.ManualUnitValueBeforeVat.HasValue && decimal.Round(x.ManualUnitValueBeforeVat.Value, 12) != x.ManualUnitValueBeforeVat.Value))
            throw new BusinessRuleException("Độ chính xác số lượng hoặc giá vượt giới hạn kế hoạch.");
    }
    private static void RequireValid(PurchaseReceiptPricingAllocationPreview result, bool allowTotalMismatch)
    {
        var errors = result.Errors.Where(x => !allowTotalMismatch || !x.StartsWith("Chênh lệch tiền hàng:", StringComparison.Ordinal)).ToArray();
        if (errors.Length > 0) throw new BusinessRuleException(string.Join(" ", errors));
        if (result.Lines.Count == 0 || result.SystemTotal <= 0) throw new BusinessRuleException("Kế hoạch chưa có kết quả giá hợp lệ.");
    }
    private static void EnsureCalculatedEvidence(PurchaseReceiptPricingPlan plan, PurchaseReceiptPricingAllocationPreview result)
    {
        var saved = plan.Lines.Where(x => !x.IsDeleted).ToDictionary(x => x.StockDocumentLineId);
        if (plan.SystemTotal != result.SystemTotal || saved.Count != result.Lines.Count || result.Lines.Any(x =>
                !saved.TryGetValue(x.StockDocumentLineId, out var line) || line.FinalAmountBeforeVat != x.FinalAmountBeforeVat || line.BaselineAmount != x.BaselineAmount ||
                line.GiftBurden != x.GiftBurden || line.PurchasedAmount != x.PurchasedAmount || line.GiftAmount != x.GiftAmount))
            throw Conflict("Bằng chứng tính giá không còn khớp kế hoạch. Tính lại và lưu trước khi Apply.");
    }

    private static PurchaseReceiptPricingAllocationRequest DefaultDraft(StockDocument receipt, IReadOnlyList<PurchaseReceiptPricingPhysicalLine> context)
    {
        var lines = context.Select(x => new PurchaseReceiptPricingLineInput { StockDocumentLineId = x.StockDocumentLineId, RowVersion = x.RowVersion,
            BillUnitId = x.UnitId, BillQuantity = x.Quantity, BillUnitPriceBeforeVat = x.CurrentUnitPriceBeforeVat }).ToList();
        return new() { ReceiptRowVersion = Version(receipt.RowVersion), Lines = lines, BillLines = [], ActualBillTotal = 0m };
    }
    private static List<PurchaseReceiptPricingGiftValue> Values(PurchaseReceiptPricingPlan plan)
        => plan.GiftValuations.Where(x => !x.IsDeleted).Select(x => new PurchaseReceiptPricingGiftValue(x.ProductVariantId, x.UnitId, x.Factor,
            x.UnitValueBeforeVat, x.Source, x.HistoricalReceiptLineId, x.HistoricalConfirmedAtUtc)).ToList();
    private static PurchaseReceiptPricingAllocationRequest ToRequest(PurchaseReceiptPricingPlan plan)
    {
        var active = plan.Lines.Where(x => !x.IsDeleted).ToDictionary(x => x.Id);
        var bill = plan.BillLines.Where(x => !x.IsDeleted).ToDictionary(x => x.Id);
        return new()
        {
            ReceiptRowVersion = Version(plan.ReceiptRowVersionSnapshot), PlanRowVersion = Version(plan.RowVersion), ActualBillTotal = plan.ActualBillTotal,
            GlobalDiscountPercent = plan.GlobalDiscountPercent,
            BillLines = plan.BillLayoutVersion == 1 ? bill.Values.OrderBy(x => x.LineNo).Select(x => new PurchaseReceiptBillLineInput
            { BillLineKey = x.BillLineKey, LineNo = x.LineNo, ProductVariantId = x.ProductVariantIdSnapshot, BillUnitId = x.BillUnitId,
                BillQuantity = x.BillQuantity, BillUnitPriceBeforeVat = x.BillUnitPriceBeforeVat, IsGift = x.IsGift }).ToList() : null,
            Lines = active.Values.OrderBy(x => x.LineNo).Select(x => new PurchaseReceiptPricingLineInput { StockDocumentLineId = x.StockDocumentLineId,
                RowVersion = Version(x.ReceiptLineRowVersionSnapshot), BillUnitId = x.BillUnitId, BillQuantity = x.BillQuantity, BillUnitPriceBeforeVat = x.BillUnitPriceBeforeVat }).ToList(),
            Rules = plan.Rules.Where(x => !x.IsDeleted).OrderBy(x => x.RuleKey).Select(x => new PurchaseReceiptPricingRuleInput { RuleKey = x.RuleKey,
                Name = x.Name, ProgramKey = x.ProgramKey, Type = x.Type, DiscountPercent = x.DiscountPercent, GiftMode = x.GiftMode ?? PurchaseReceiptPricingGiftMode.DifferentSku,
                DiscountAmount = x.Type == PurchaseReceiptPricingRuleType.FixedAmountDiscount ? x.Amount : 0m,
                GiftLineId = x.GiftPlanLineId.HasValue ? active[x.GiftPlanLineId.Value].StockDocumentLineId : null, GiftUnitId = x.GiftUnitId, GiftQuantity = x.GiftQuantity,
                SourceLineIds = x.Sources.Where(s => !s.IsDeleted).Select(s => active[s.PricingPlanLineId].StockDocumentLineId).Distinct().ToList(),
                GiftBillLineKey = x.GiftBillLineId.HasValue ? bill[x.GiftBillLineId.Value].BillLineKey : null,
                BillSources = x.Sources.Where(s => !s.IsDeleted && s.BillLineId.HasValue).Select(s => new PurchaseReceiptBillRuleSourceInput
                { BillLineKey = bill[s.BillLineId!.Value].BillLineKey, Quantity = s.ParticipatingQuantity!.Value }).ToList() }).ToList(),
            GiftValuations = plan.GiftValuations.Where(x => !x.IsDeleted && x.Source != PurchaseReceiptGiftValuationSource.SameSkuBlend)
                .Select(x => new PurchaseReceiptGiftValuationInput { ProductVariantId = x.ProductVariantId, UnitId = x.UnitId,
                    ManualUnitValueBeforeVat = x.Source == PurchaseReceiptGiftValuationSource.Manual ? x.UnitValueBeforeVat : null }).ToList()
        };
    }

    private static void ReplaceGraph(PurchaseReceiptPricingPlan plan, PurchaseReceiptPricingAllocationRequest request,
        IReadOnlyList<PurchaseReceiptPricingPhysicalLine> context, PurchaseReceiptPricingAllocationPreview result)
    {
        foreach (var old in plan.Lines) old.IsDeleted = true;
        foreach (var old in plan.Rules) { old.IsDeleted = true; foreach (var source in old.Sources) source.IsDeleted = true; }
        foreach (var old in plan.GiftValuations) old.IsDeleted = true;
        foreach (var old in plan.BillLines) old.IsDeleted = true;
        plan.BillLayoutVersion = request.BillLines is null ? 0 : 1;
        var billLines = new Dictionary<string, PurchaseReceiptBillLine>(StringComparer.Ordinal);
        foreach (var input in request.BillLines ?? [])
        {
            var unit = context.First(x => x.ProductVariantId == input.ProductVariantId).Units.Single(x => x.UnitId == input.BillUnitId);
            var savedBill = new PurchaseReceiptBillLine { StoreId = plan.StoreId, PricingPlan = plan, BillLineKey = input.BillLineKey,
                LineNo = input.LineNo, ProductVariantIdSnapshot = input.ProductVariantId, BillUnitId = input.BillUnitId, BillUnitName = unit.Name,
                BillConversionId = unit.ConversionId, BillFactor = unit.Factor, BillQuantity = input.BillQuantity,
                BillUnitPriceBeforeVat = input.BillUnitPriceBeforeVat, IsGift = input.IsGift };
            plan.BillLines.Add(savedBill); billLines.Add(input.BillLineKey, savedBill);
        }
        var inputs = request.Lines.ToDictionary(x => x.StockDocumentLineId);
        var physical = context.ToDictionary(x => x.StockDocumentLineId);
        var lines = new Dictionary<int, PurchaseReceiptPricingPlanLine>();
        foreach (var value in result.Lines)
        {
            var line = physical[value.StockDocumentLineId]; var input = request.BillLines is null ? inputs[line.StockDocumentLineId] :
                new PurchaseReceiptPricingLineInput { BillUnitId = line.UnitId, BillQuantity = line.Quantity, BillUnitPriceBeforeVat = line.CurrentUnitPriceBeforeVat };
            var billUnit = line.Units.Single(x => x.UnitId == input.BillUnitId);
            var saved = new PurchaseReceiptPricingPlanLine { StoreId = plan.StoreId, PricingPlan = plan, StockDocumentLineId = line.StockDocumentLineId,
                LineNo = line.LineNo, ProductVariantIdSnapshot = line.ProductVariantId, ProductIdSnapshot = line.ProductId,
                PhysicalUnitIdSnapshot = line.UnitId, PhysicalConversionIdSnapshot = line.ProductUnitConversionId, PhysicalFactorSnapshot = line.Factor,
                PhysicalQuantitySnapshot = line.Quantity, PhysicalBaseQuantitySnapshot = line.BaseQuantity, ReceiptLineRowVersionSnapshot = Convert.FromBase64String(line.RowVersion),
                BillUnitId = input.BillUnitId, BillConversionId = billUnit.ConversionId, BillFactor = billUnit.Factor, BillQuantity = input.BillQuantity,
                BillUnitPriceBeforeVat = input.BillUnitPriceBeforeVat, PurchasedBaseQuantity = value.PurchasedBaseQuantity, GiftBaseQuantity = value.GiftBaseQuantity,
                BaselineAmount = value.BaselineAmount, BaselineResidual = result.Residuals.Where(x => x.Stage == "baseline" && x.StockDocumentLineId == line.StockDocumentLineId).Sum(x => x.Amount),
                GiftBurden = value.GiftBurden, PurchasedAmount = value.PurchasedAmount, GiftAmount = value.GiftAmount, FinalAmountBeforeVat = value.FinalAmountBeforeVat,
                EffectiveUnitPriceBeforeVat = value.EffectiveUnitPriceBeforeVat };
            plan.Lines.Add(saved); lines.Add(line.StockDocumentLineId, saved);
        }
        foreach (var rule in request.Rules)
        {
            var calculated = result.Rules.Single(x => x.RuleKey == rule.RuleKey);
            var gift = rule.Type == PurchaseReceiptPricingRuleType.Gift;
            var giftUnit = gift ? physical[rule.GiftLineId!.Value].Units.Single(x => x.UnitId == rule.GiftUnitId) : null;
            var saved = new PurchaseReceiptPricingRule { StoreId = plan.StoreId, PricingPlan = plan, RuleKey = rule.RuleKey, Name = rule.Name ?? "", ProgramKey = rule.ProgramKey ?? "", Type = rule.Type,
                GiftBillLine = !string.IsNullOrEmpty(rule.GiftBillLineKey) ? billLines[rule.GiftBillLineKey] : null,
                DiscountPercent = gift ? 0m : rule.DiscountPercent, GiftMode = gift ? rule.GiftMode : null,
                GiftPlanLine = gift ? lines[rule.GiftLineId!.Value] : null, GiftUnitId = giftUnit?.UnitId, GiftConversionId = giftUnit?.ConversionId,
                GiftFactor = giftUnit?.Factor ?? 0m, GiftQuantity = gift ? rule.GiftQuantity : 0m, Amount = calculated.Amount };
            if (request.BillLines is not null)
            {
                foreach (var source in rule.BillSources)
                {
                    var billLine = billLines[source.BillLineKey];
                    var representative = context.Where(x => x.ProductVariantId == billLine.ProductVariantIdSnapshot)
                        .OrderBy(x => x.LineNo).ThenBy(x => x.StockDocumentLineId).First();
                    var evidence = result.BillSources.Single(x => x.RuleKey == rule.RuleKey && x.BillLineKey == source.BillLineKey);
                    saved.Sources.Add(new() { StoreId = plan.StoreId, PricingRule = saved, PricingPlanLine = lines[representative.StockDocumentLineId],
                        BillLine = billLine, ParticipatingQuantity = source.Quantity, BaselineAmountSnapshot = evidence.BaselineAmount,
                        Amount = evidence.Amount, ResidualAmount = evidence.ResidualAmount });
                }
            }
            else foreach (var id in rule.SourceLineIds)
                saved.Sources.Add(new() { StoreId = plan.StoreId, PricingRule = saved, PricingPlanLine = lines[id],
                    BaselineAmountSnapshot = lines[id].BaselineAmount, Amount = calculated.SourceAmounts[id],
                    ResidualAmount = result.Residuals.Where(x => (x.Stage == "gift" || x.Stage == "discount") &&
                        x.RuleKey == rule.RuleKey && x.StockDocumentLineId == id).Sum(x => x.Amount) });
            plan.Rules.Add(saved);
        }
        foreach (var value in result.GiftValues)
        {
            var unit = context.First(x => x.ProductVariantId == value.ProductVariantId).Units.Single(x => x.UnitId == value.UnitId);
            plan.GiftValuations.Add(new() { StoreId = plan.StoreId, PricingPlan = plan, ProductVariantId = value.ProductVariantId, UnitId = value.UnitId,
                ConversionId = unit.ConversionId, Factor = value.Factor, UnitValueBeforeVat = decimal.Round(value.UnitValueBeforeVat, 12, MidpointRounding.AwayFromZero),
                Source = value.Source, HistoricalReceiptLineId = value.HistoricalReceiptLineId, HistoricalConfirmedAtUtc = value.HistoricalConfirmedAtUtc });
        }
    }
}
