using FluentAssertions;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Services.Purchases;

namespace GaoApp.Tests.Purchases;

public sealed class PurchaseReceiptSupplierInvoiceConsistencyPolicyTests
{
    [Fact]
    public void No_linked_invoice_or_same_supplier_should_be_allowed()
    {
        PurchaseReceiptSupplierInvoiceConsistencyPolicy.EnsureCanConfirm(7, []);
        PurchaseReceiptSupplierInvoiceConsistencyPolicy.EnsureCanConfirm(7, [7, 7]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(8)]
    public void Null_or_different_canonical_supplier_should_be_blocked(int? canonicalSupplierId)
    {
        var action = () => PurchaseReceiptSupplierInvoiceConsistencyPolicy
            .EnsureCanConfirm(7, [canonicalSupplierId]);

        action.Should().Throw<BusinessRuleException>();
    }
}
