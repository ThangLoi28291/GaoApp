using FluentAssertions;
using GaoApp.Application.Services.Purchases;

namespace GaoApp.Tests.Purchases;

public sealed class PurchaseReceiptWarehouseDatePolicyTests
{
    [Fact]
    public void Linked_valid_invoice_uses_invoice_date_without_local_conversion()
    {
        var invoiceDate = new DateTime(2026, 8, 20, 23, 30, 0, DateTimeKind.Utc);
        var result = PurchaseReceiptWarehouseDatePolicy.Resolve(
            new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Local),
            [invoiceDate]);

        result.Should().Be(new DateTime(2026, 8, 20));
        result.Kind.Should().Be(DateTimeKind.Unspecified);
    }

    [Fact]
    public void No_invoice_uses_document_date_for_source_and_child()
    {
        var inheritedDocumentDate = new DateTime(2026, 7, 31, 18, 0, 0, DateTimeKind.Utc);

        PurchaseReceiptWarehouseDatePolicy.Resolve(inheritedDocumentDate, [])
            .Should().Be(new DateTime(2026, 7, 31));
    }

    [Fact]
    public void Multiple_active_invoice_dates_are_rejected_as_corruption()
    {
        var action = () => PurchaseReceiptWarehouseDatePolicy.Resolve(
            new DateTime(2026, 8, 1),
            [new DateTime(2026, 8, 2), new DateTime(2026, 8, 3)]);

        action.Should().Throw<InvalidOperationException>().WithMessage("*nhiều hóa đơn*");
    }
}
