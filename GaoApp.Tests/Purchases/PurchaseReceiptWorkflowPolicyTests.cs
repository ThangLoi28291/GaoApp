using FluentAssertions;
using GaoApp.Application.Services.Purchases;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.Purchases;

public sealed class PurchaseReceiptWorkflowPolicyTests
{
    [Theory]
    [InlineData(StockDocumentStatus.Draft, true)]
    [InlineData(StockDocumentStatus.PendingApproval, false)]
    [InlineData(StockDocumentStatus.Confirmed, false)]
    [InlineData(StockDocumentStatus.Rejected, true)]
    [InlineData(StockDocumentStatus.Cancelled, false)]
    public void Physical_editability_matches_the_locked_status_contract(
        StockDocumentStatus status,
        bool expected)
    {
        PurchaseReceiptWorkflowPolicy.CanEditPhysicalFields(status)
            .Should().Be(expected);
    }

    [Fact]
    public void Unknown_status_is_not_physically_editable()
    {
        PurchaseReceiptWorkflowPolicy.CanEditPhysicalFields(
                (StockDocumentStatus)int.MaxValue)
            .Should().BeFalse();
    }
}
