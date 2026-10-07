using GaoApp.Application.DTOs.POS;
using GaoApp.Web.Services.StoreMonitor;

namespace GaoApp.Tests.Observability;

public sealed class StoreActivityDetailsTests
{
    [Fact]
    public void Pos_summary_identifies_saved_goods_quantity_and_total_without_customer_or_typed_notes()
    {
        var draft = new OrderDraftDto { OrderId=42, GrandTotal=120000, CustomerName="PRIVATE CUSTOMER", CustomerPhone="PRIVATE PHONE", Note="PRIVATE NOTE",
            Lines=[new OrderLineDto { VariantId=10, ItemName="Gạo ST25", Quantity=2 }] };
        var detail = StoreActivityDetails.Describe("POS", "AddItem", new Dictionary<string,object?> { ["variantId"]=10, ["qty"]=2m }, draft);
        Assert.Equal("#42", detail.Document);
        Assert.Contains("Gạo ST25", detail.Detail); Assert.Contains("Số lượng 2", detail.Detail); Assert.Contains("120.000 ₫", detail.Detail);
        Assert.DoesNotContain("PRIVATE", detail.Detail);
    }
    [Fact]
    public void Receipt_line_id_cannot_replace_parent_document_and_body_header_id_is_supported()
    {
        var added = StoreActivityDetails.Describe("StockDocuments", "AddLine", new Dictionary<string,object?> { ["id"]=42, ["request"]=new { Quantity=3m } }, new { lineId=999 });
        Assert.Equal("#42", added.Document); Assert.Contains("Số lượng 3", added.Detail);
        var header = StoreActivityDetails.Describe("StockDocumentManagement", "UpdateHeader", new Dictionary<string,object?> { ["request"]=new { StockDocumentId=43, Note="PRIVATE" } }, new { success=true, SupplierName="PRIVATE" });
        Assert.Equal("#43", header.Document); Assert.Null(header.Detail);
    }
    [Fact]
    public void Wrapped_checkout_response_keeps_parent_order_and_payment_amount()
    {
        var detail = StoreActivityDetails.Describe("POS", "AddPaymentAndMaybeFinalizeCurrentCart", new Dictionary<string,object?> { ["request"]=new { Amount=100000m } }, new { finalized=true, draft=new OrderDraftDto { OrderId=42, GrandTotal=100000 } });
        Assert.Equal("#42", detail.Document); Assert.Contains("Ghi nhận 100.000 ₫", detail.Detail);
    }
    [Fact]
    public void Label_confirmation_is_a_print_job_not_an_order_and_counts_only_allowed_quantity_fields()
    {
        var detail = StoreActivityDetails.Describe("LabelPrinting", "Confirm", new Dictionary<string,object?> { ["id"]=55 }, new { success=true });
        Assert.Equal("Lệnh in #55", detail.Document);
        var print = StoreActivityDetails.Describe("LabelPrinting", "Print", new Dictionary<string,object?> { ["request"]=new { Lines=new[] { new { Quantity=12, Note="PRIVATE" } } } }, new { id=56 });
        Assert.Equal("Lệnh in #56", print.Document); Assert.Equal("1 mặt hàng · 12 tem", print.Detail);
    }
    [Fact]
    public void Warehouse_count_zero_and_transfer_quantity_are_explicit_without_confusing_line_with_document()
    {
        var counted = StoreActivityDetails.Describe("StockCounts", "UpdateLine", new Dictionary<string,object?> { ["lineId"]=99, ["request"]=new { CountedQty=0m } }, new { message="Saved" });
        Assert.Equal("Dòng kiểm kê #99", counted.Document); Assert.Equal("Đã đếm 0", counted.Detail);
        var transfer = StoreActivityDetails.Describe("StockTransfers", "AddLine", new Dictionary<string,object?> { ["id"]=42, ["request"]=new { Quantity=5m } }, new { id=999 });
        Assert.Equal("#42", transfer.Document); Assert.Equal("Số lượng 5", transfer.Detail);
    }
    [Fact]
    public void Hold_current_cart_identifies_held_order_instead_of_new_empty_cart()
    {
        var held = StoreActivityDetails.Describe("POS", "HoldCurrentCart", new Dictionary<string,object?>(), new HoldOrderResultDto { HeldOrderId=42, NewDraftOrderId=99 });
        Assert.Equal("#42", held.Document); Assert.Null(held.Detail);
    }
    [Fact]
    public void Work_keys_separate_document_types_and_a_label_print_keeps_its_parent_task()
    {
        var args = new Dictionary<string,object?> { ["id"]=42 };
        Assert.Equal("receipt:42", StoreActivityDetails.Describe("StockDocuments", "SubmitApproval", args, null).WorkKey);
        Assert.Equal("count:42", StoreActivityDetails.Describe("StockCounts", "Create", new Dictionary<string,object?>(), new { id=42 }).WorkKey);
        Assert.Equal("transfer:42", StoreActivityDetails.Describe("StockTransfers", "AddLine", args, new { id=99 }).WorkKey);
        Assert.Equal("label-job:42", StoreActivityDetails.Describe("LabelPrinting", "Print", new Dictionary<string,object?>(), new { id=42 }).WorkKey);
        Assert.Equal("label:42", StoreActivityDetails.Describe("LabelPrinting", "Print", new Dictionary<string,object?> { ["request"]=new { TaskId=42 } }, new { id=99 }).WorkKey);
    }
}
