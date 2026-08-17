using FluentAssertions;
using GaoApp.Application.Services.Purchases;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.Purchases;

public sealed class PurchaseReceiptPolicyTests
{
    [Fact]
    public void Full_receipt_should_clear_pending_and_finish_line()
    {
        var line = CreateLine(10m);

        PurchaseReceiptPolicy.ApplyApprovedLine(
            line, 10m, PurchaseShortageDisposition.None, null, DateTime.UtcNow, 7);

        line.ReceivedQuantity.Should().Be(10m);
        line.PendingQuantity.Should().Be(0m);
        line.ShortClosedQuantity.Should().Be(0m);
        line.ReceiptStatus.Should().Be(PurchaseOrderLineReceiptStatus.FullyReceived);
    }

    [Fact]
    public void Partial_wait_should_keep_the_remainder_pending()
    {
        var line = CreateLine(10m);

        PurchaseReceiptPolicy.ApplyApprovedLine(
            line, 4m, PurchaseShortageDisposition.WaitForBackorder, null, DateTime.UtcNow, 7);

        line.ReceivedQuantity.Should().Be(4m);
        line.PendingQuantity.Should().Be(6m);
        line.ReceiptStatus.Should().Be(PurchaseOrderLineReceiptStatus.PartiallyReceived);
    }

    [Fact]
    public void Second_receipt_should_complete_a_waiting_line()
    {
        var line = CreateLine(10m);
        PurchaseReceiptPolicy.ApplyApprovedLine(
            line, 4m, PurchaseShortageDisposition.WaitForBackorder, null, DateTime.UtcNow, 7);

        PurchaseReceiptPolicy.ApplyApprovedLine(
            line, 6m, PurchaseShortageDisposition.None, null, DateTime.UtcNow, 8);

        line.ReceivedQuantity.Should().Be(10m);
        line.PendingQuantity.Should().Be(0m);
        line.ReceiptStatus.Should().Be(PurchaseOrderLineReceiptStatus.FullyReceived);
    }

    [Fact]
    public void Short_close_should_store_quantity_reason_actor_and_time()
    {
        var line = CreateLine(10m);
        var occurredAt = new DateTime(2026, 7, 19, 3, 0, 0, DateTimeKind.Utc);

        PurchaseReceiptPolicy.ApplyApprovedLine(
            line, 7m, PurchaseShortageDisposition.ShortClose, "Nhà cung cấp hết hàng", occurredAt, 9);

        line.ReceivedQuantity.Should().Be(7m);
        line.ShortClosedQuantity.Should().Be(3m);
        line.PendingQuantity.Should().Be(0m);
        line.ShortCloseReason.Should().Be("Nhà cung cấp hết hàng");
        line.ShortClosedAtUtc.Should().Be(occurredAt);
        line.ShortClosedByUserId.Should().Be(9);
        line.ReceiptStatus.Should().Be(PurchaseOrderLineReceiptStatus.ShortClosed);
    }

    [Fact]
    public void Partial_receipt_without_disposition_should_be_blocked()
    {
        var action = () => PurchaseReceiptPolicy.ValidateLine(
            10m, 4m, PurchaseShortageDisposition.None, null, 1);

        action.Should().Throw<InvalidOperationException>().WithMessage("*phải chọn*");
    }

    [Fact]
    public void Short_close_without_reason_should_be_blocked()
    {
        var action = () => PurchaseReceiptPolicy.ValidateLine(
            10m, 4m, PurchaseShortageDisposition.ShortClose, " ", 1);

        action.Should().Throw<InvalidOperationException>().WithMessage("*lý do*");
    }

    [Fact]
    public void Over_receipt_is_valid_for_draft_and_normalizes_shortage_disposition()
    {
        var decision = PurchaseReceiptPolicy.ValidateLine(
            10m, 10.001m, PurchaseShortageDisposition.ShortClose, "ignored", 1);

        decision.ReceivedQuantity.Should().Be(10.001m);
        decision.ShortageDisposition.Should().Be(PurchaseShortageDisposition.None);
        decision.ShortageReason.Should().BeNull();
    }

    [Fact]
    public void Completed_line_quantity_can_be_prepared_for_managed_overdelivery()
    {
        var decision = PurchaseReceiptPolicy.ValidateLine(
            0m, 1m, PurchaseShortageDisposition.None, null, 1);

        decision.PendingBefore.Should().Be(0m);
        decision.ReceivedQuantity.Should().Be(1m);
    }

    [Fact]
    public void Overdelivery_decision_reports_only_the_increment_added_by_this_confirmation()
    {
        var crossing = PurchaseReceiptPolicy.EvaluateOverdelivery(10m, 9m, 3m, 4);
        var alreadyOver = PurchaseReceiptPolicy.EvaluateOverdelivery(10m, 12m, 1.5m, 4);

        crossing.IncrementalCanonicalQuantity.Should().Be(2m);
        crossing.CanonicalQuantityAfterConfirmation.Should().Be(2m);
        alreadyOver.IncrementalCanonicalQuantity.Should().Be(1.5m);
        alreadyOver.CanonicalQuantityAfterConfirmation.Should().Be(3.5m);
    }

    [Fact]
    public void Order_status_should_distinguish_partial_full_and_short_closed()
    {
        var first = CreateLine(10m, lineNo: 1);
        var second = CreateLine(5m, lineNo: 2);
        var lines = new[] { first, second };

        PurchaseReceiptPolicy.ApplyApprovedLine(first, 4m, PurchaseShortageDisposition.WaitForBackorder, null, DateTime.UtcNow, 1);
        PurchaseReceiptPolicy.ResolveOrderStatus(lines, PurchaseOrderStatus.SentToSupplier)
            .Should().Be(PurchaseOrderStatus.PartiallyReceived);

        PurchaseReceiptPolicy.ApplyApprovedLine(first, 6m, PurchaseShortageDisposition.None, null, DateTime.UtcNow, 1);
        PurchaseReceiptPolicy.ApplyApprovedLine(second, 5m, PurchaseShortageDisposition.None, null, DateTime.UtcNow, 1);
        PurchaseReceiptPolicy.ResolveOrderStatus(lines, PurchaseOrderStatus.PartiallyReceived)
            .Should().Be(PurchaseOrderStatus.FullyReceived);

        var shortLine = CreateLine(3m);
        PurchaseReceiptPolicy.ApplyApprovedLine(shortLine, 2m, PurchaseShortageDisposition.ShortClose, "Thiếu", DateTime.UtcNow, 1);
        PurchaseReceiptPolicy.ResolveOrderStatus(new[] { shortLine }, PurchaseOrderStatus.PartiallyReceived)
            .Should().Be(PurchaseOrderStatus.ShortClosed);
    }

    [Fact]
    public void Payable_keys_should_be_deterministic_and_separate_by_source_type()
    {
        PurchasePostingIdentity.MerchandisePayable(42).Should().Be("STOCK:42:MERCHANDISE");
        PurchasePostingIdentity.FreightPayable(42).Should().Be("STOCK:42:FREIGHT");
        PurchasePostingIdentity.MerchandisePayable(42)
            .Should().NotBe(PurchasePostingIdentity.FreightPayable(42));
    }

    private static PurchaseOrderLine CreateLine(decimal orderedQuantity, int lineNo = 1)
        => new()
        {
            LineNo = lineNo,
            OrderedQuantity = orderedQuantity,
            ReceiptStatus = PurchaseOrderLineReceiptStatus.NotReceived
        };
}
