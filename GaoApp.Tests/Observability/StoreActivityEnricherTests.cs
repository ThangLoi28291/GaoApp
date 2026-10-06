using GaoApp.Infrastructure.Printing;
using GaoApp.Web.Services.StoreMonitor;

namespace GaoApp.Tests.Observability;

public sealed class StoreActivityEnricherTests
{
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
