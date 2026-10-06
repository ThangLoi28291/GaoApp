using GaoApp.Infrastructure.Printing;
using GaoApp.Domain.Entities;
using GaoApp.Tests.Configuration;
using GaoApp.Web.Services.StoreMonitor;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Observability;

[Collection("R1FinalDatabasePreflight")]
public sealed class StoreActivityEnricherTests
{
    [Fact]
    public async Task Count_and_transfer_capture_first_original_and_last_saved_values_with_parent_and_store_bounds()
    {
        await using var database = new InventoryPostingLocalDb(); await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync();
        int countId, countLineId, transferId, transferLineId;
        await using (var db = database.CreateTenantContext(seed.StoreId))
        {
            var unit = await db.Units.SingleAsync(); unit.Name="kg";
            var source = await db.Warehouses.SingleAsync();
            var destination = new Warehouse { StoreId=seed.StoreId, LegalEntityId=source.LegalEntityId,
                Code="MONITOR-DEST", Name="Destination", IsActive=true };
            db.Add(destination); await db.SaveChangesAsync();
            var count = new StockCountDocument { StoreId=seed.StoreId, WarehouseId=source.Id, DocumentNo="MONITOR-COUNT" };
            count.Lines.Add(new() { StoreId=seed.StoreId, ProductVariantId=seed.ProductVariantId, UnitId=unit.Id,
                ProductNameSnapshot="Count before", UnitNameSnapshot=null, LineNo=1, Factor=1,
                SystemQtyBase=5, CountedQty=1, CountedQtyBase=1, DifferenceQtyBase=-4 });
            var transfer = new StockTransferDocument { StoreId=seed.StoreId, FromWarehouseId=source.Id,
                ToWarehouseId=destination.Id, DocumentNo="MONITOR-TRANSFER", DocumentDate=DateTime.Today };
            transfer.Lines.Add(new() { StoreId=seed.StoreId, ProductVariantId=seed.ProductVariantId, UnitId=unit.Id,
                ProductNameSnapshot="Transfer before", UnitNameSnapshot="kg", LineNo=1, Factor=1, Quantity=1, BaseQuantity=1 });
            db.AddRange(count, transfer); await db.SaveChangesAsync();
            countId=count.Id; countLineId=count.Lines.Single().Id; transferId=transfer.Id; transferLineId=transfer.Lines.Single().Id;
        }
        await using var operationDb = database.CreateTenantContext(seed.StoreId);
        var countLine = await operationDb.StockCountLines.Include(x => x.StockCountDocument).SingleAsync(x => x.Id == countLineId);
        var transferLine = await operationDb.StockTransferLines.Include(x => x.StockTransferDocument).SingleAsync(x => x.Id == transferLineId);
        using var operation = new StoreActivityEnricher.SavedOperation(operationDb, seed.StoreId, includeBefore:true);
        using var foreign = new StoreActivityEnricher.SavedOperation(operationDb, seed.StoreId+100000, includeBefore:true);
        countLine.CountedQty=countLine.CountedQtyBase=2; countLine.DifferenceQtyBase=-3;
        countLine.ProductNameSnapshot="Count saved"; countLine.UnitNameSnapshot="kg";
        transferLine.Quantity=transferLine.BaseQuantity=2; transferLine.ProductNameSnapshot="Transfer saved";
        await operationDb.SaveChangesAsync();
        countLine.CountedQty=countLine.CountedQtyBase=3; countLine.DifferenceQtyBase=-2;
        transferLine.Quantity=transferLine.BaseQuantity=3; await operationDb.SaveChangesAsync();
        // Later unsaved mutations must not affect either immutable side of the completed operation.
        countLine.CountedQty=transferLine.Quantity=999; countLine.DifferenceQtyBase=999;
        var countBefore = await operation.Before("StockCounts", countLineId, countId, default);
        var transferBefore = await operation.Before("StockTransfers", transferLineId, transferId, default);
        Assert.NotNull(countBefore); Assert.Equal("Count before", countBefore.Name); Assert.Equal(1m, countBefore.Quantity);
        Assert.Equal("kg", countBefore.Unit); Assert.Equal(-4m, countBefore.DifferenceBase);
        Assert.NotNull(transferBefore); Assert.Equal("Transfer before", transferBefore.Name); Assert.Equal(1m, transferBefore.Quantity);
        Assert.Equal("kg", transferBefore.Unit);
        var countAfter = await StoreActivityEnricher.Read(operationDb, seed.StoreId, "StockCounts", "UpdateLine",
            new Dictionary<string,object?> { ["lineId"]=countLineId }, null, null, default, operation);
        var transferAfter = await StoreActivityEnricher.Read(operationDb, seed.StoreId, "StockTransfers", "UpdateLine",
            new Dictionary<string,object?> { ["lineId"]=transferLineId }, null, null, default, operation);
        Assert.NotNull(countAfter); Assert.NotNull(transferAfter);
        Assert.Equal(3m, countAfter.Line!.Quantity); Assert.Equal(-2m, countAfter.Line.DifferenceBase);
        Assert.Equal("Count saved", countAfter.Line.Name); Assert.Equal(3m, transferAfter.Line!.Quantity);
        var countActivity = StoreActivityEnricher.Describe("StockCounts", "UpdateLine", countAfter, countAfter with { Line=countBefore });
        var transferActivity = StoreActivityEnricher.Describe("StockTransfers", "UpdateLine", transferAfter, transferAfter with { Line=transferBefore });
        Assert.Contains("1 kg → 3 kg", countActivity.Detail); Assert.Contains("Chênh lệch -2 ĐV gốc", countActivity.Detail);
        Assert.Contains("1 kg → 3 kg", transferActivity.Detail);
        Assert.Null(await operation.Before("StockCounts", countLineId, countId+100000, default));
        Assert.Null(await operation.Before("StockDocuments", countLineId, countId, default));
        Assert.Null(await foreign.Before("StockCounts", countLineId, countId, default));
        Assert.Null(await foreign.Before("StockTransfers", transferLineId, transferId, default));
        await using var staleDb = database.CreateTenantContext(seed.StoreId);
        var staleLine = await staleDb.StockCountLines.Include(x => x.StockCountDocument).SingleAsync(x => x.Id == countLineId);
        using var failed = new StoreActivityEnricher.SavedOperation(staleDb, seed.StoreId, includeBefore:true);
        await using (var writer = database.CreateTenantContext(seed.StoreId))
        {
            var concurrent = await writer.StockCountLines.SingleAsync(x => x.Id == countLineId);
            concurrent.CountedQty=concurrent.CountedQtyBase=4; concurrent.DifferenceQtyBase=-1;
            await writer.SaveChangesAsync();
        }
        staleLine.CountedQty=staleLine.CountedQtyBase=5; staleLine.DifferenceQtyBase=0;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleDb.SaveChangesAsync());
        Assert.Null(await failed.Before("StockCounts", countLineId, countId, default));
        var failedAfter = await StoreActivityEnricher.Read(staleDb, seed.StoreId, "StockCounts", "UpdateLine",
            new Dictionary<string,object?> { ["stockCountDocumentId"]=countId, ["lineId"]=countLineId }, null, null, default, failed);
        Assert.NotNull(failedAfter); Assert.Null(failedAfter.Line);
        await using var addDb = database.CreateTenantContext(seed.StoreId);
        var parent = await addDb.StockCountDocuments.SingleAsync(x => x.Id == countId);
        var added = new StockCountLine { StoreId=seed.StoreId, StockCountDocument=parent, ProductVariantId=seed.ProductVariantId,
            UnitId=staleLine.UnitId, ProductNameSnapshot="Added", UnitNameSnapshot="kg", LineNo=2,
            Factor=1, CountedQty=4, CountedQtyBase=4, DifferenceQtyBase=4 };
        using var addition = new StoreActivityEnricher.SavedOperation(addDb, seed.StoreId, includeBefore:true);
        addDb.Add(added); await addDb.SaveChangesAsync();
        Assert.Null(await addition.Before("StockCounts", added.Id, countId, default));
        var addedAfter = await StoreActivityEnricher.Read(addDb, seed.StoreId, "StockCounts", "AddLine",
            new Dictionary<string,object?> { ["stockCountDocumentId"]=countId }, new { id=added.Id }, null, default, addition);
        Assert.Equal(4m, addedAfter!.Line!.Quantity);
    }
    private static StoreActivityEnricher.Snapshot Receipt(decimal quantity = 5, string unit = "kg") =>
        new(42, "NH-001", "Kho chính", new("Gạo ST25", quantity, unit), 1, "Gạo ST25");
    [Theory]
    [InlineData("AddLine", "thêm")]
    [InlineData("AddLineByBarcode", "quét")]
    public void Receipt_names_are_saved_goods_with_units_and_parent(string action, string verb)
    {
        var result = StoreActivityEnricher.Describe("StockDocuments", action, Receipt());
        Assert.Contains(verb, result.Text); Assert.Contains("Gạo ST25", result.Text);
        Assert.Contains("Số lượng 5 kg", result.Detail); Assert.Contains("NH-001", result.Detail);
        Assert.Equal("receipt:42", result.WorkKey); Assert.Equal("Phiếu nhập #42", result.Document);
    }
    [Fact]
    public void Edits_show_old_and_new_units_and_deleted_rows_keep_old_saved_name()
    {
        var edited = StoreActivityEnricher.Describe("StockDocuments", "UpdateLine", Receipt(2, "bao"), Receipt());
        Assert.Contains("5 kg → 2 bao", edited.Detail);
        var removed = StoreActivityEnricher.Describe("StockDocuments", "DeleteLine", Receipt() with { Line = null, LineCount = 0 }, Receipt());
        Assert.Contains("Gạo ST25", removed.Text); Assert.Contains("Đã bỏ 5 kg", removed.Detail);
    }
    [Fact]
    public void Count_zero_and_difference_are_explicit_and_transfer_has_route()
    {
        var count = StoreActivityEnricher.Describe("StockCounts", "UpdateLine", Receipt(0) with { Line = new("Gạo ST25", 0, "kg", -100) }, Receipt());
        Assert.Contains("5 kg → 0 kg", count.Detail); Assert.Contains("Chênh lệch -100 ĐV gốc", count.Detail); Assert.Equal("count:42", count.WorkKey);
        var transfer = StoreActivityEnricher.Describe("StockTransfers", "AddLine", Receipt() with { Location = "Kho A → Kho B" });
        Assert.Contains("Gạo ST25", transfer.Text); Assert.Contains("Kho A → Kho B", transfer.Detail); Assert.Equal("transfer:42", transfer.WorkKey);
    }
    [Theory]
    [InlineData(true, false, "lưu thông tin chờ duyệt")]
    [InlineData(false, true, "duyệt mặt hàng")]
    [InlineData(false, false, "bổ sung thông tin")]
    public void Receipt_review_distinguishes_draft_from_approval(bool draft, bool approve, string expected)
    {
        var result = StoreActivityEnricher.Describe("ReceiptIntake", "Review", Receipt(), request: new { SaveDraftOnly=draft, Approve=approve, Note="PRIVATE", PhotoDataUrl="PRIVATE" });
        Assert.Contains(expected, result.Text); Assert.Contains("Gạo ST25", result.Text); Assert.DoesNotContain("PRIVATE", result.Detail);
    }
    [Fact]
    public void Approval_and_pricing_identify_document_goods_instead_of_generic_update()
    {
        var snapshot = Receipt() with { Line = null, LineCount = 5, Goods = "Gạo ST25, Dầu ăn" };
        var approved = StoreActivityEnricher.Describe("StockDocuments", "ApproveCommercial", snapshot);
        Assert.Contains("duyệt phiếu nhập NH-001", approved.Text); Assert.Contains("5 mặt hàng: Gạo ST25, Dầu ăn, …", approved.Detail);
        var price = StoreActivityEnricher.Describe("StockDocuments", "SavePriceDraft", snapshot, request: new { UnitCost=999, Note="PRIVATE" });
        Assert.Contains("lưu giá nháp", price.Text); Assert.DoesNotContain("999", price.Detail); Assert.DoesNotContain("PRIVATE", price.Detail);
    }
    [Fact]
    public void Print_queue_partial_confirmation_zero_and_same_product_different_units_are_truthful()
    {
        var items = new[] { new LabelPrintItem(new(10, "Gạo ST25", "1", "kg", 99, 1) { UnitId=1 }, 12), new LabelPrintItem(new(10, "Gạo ST25 bao", "2", "bao", 99, 1) { UnitId=2 }, 4) };
        var queued = StoreActivityEnricher.DescribeLabelJob("Print", 55, 42, items, []);
        Assert.Contains("gửi in 16 tem", queued.Text); Assert.DoesNotContain("xác nhận", queued.Text);
        var confirmed = StoreActivityEnricher.DescribeLabelJob("Confirm", 55, 42, items, [new(10, 8, 1), new(10, 0, 2)]);
        Assert.Contains("8/16 tem", confirmed.Text); Assert.Contains("Gạo ST25: 8 tem", confirmed.Detail); Assert.Contains("Gạo ST25 bao: 0 tem", confirmed.Detail); Assert.Equal("label:42", confirmed.WorkKey);
        Assert.Contains("hủy lệnh 16 tem", StoreActivityEnricher.DescribeLabelJob("Cancel", 55, null, items, []).Text);
    }
    [Fact]
    public void Long_saved_names_are_bounded_and_missing_names_are_explicit()
    {
        var result = StoreActivityEnricher.Describe("StockDocuments", "AddLine", Receipt() with { Line = new(new string('X', 250), 1, "kg") });
        Assert.True(result.Text!.Length < 110); Assert.Contains("…", result.Text);
        Assert.Contains("chưa có tên", StoreActivityEnricher.Describe("StockDocuments", "AddLine", Receipt() with { Line = new("", 1, null) }).Text);
    }
}
