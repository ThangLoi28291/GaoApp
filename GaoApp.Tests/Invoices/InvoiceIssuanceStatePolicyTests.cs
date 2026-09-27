using GaoApp.Application.Services.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.Invoices;

public sealed class InvoiceIssuanceStatePolicyTests
{
    [Theory]
    [InlineData(InvoiceProviderStatus.LocalDraft)]
    [InlineData(InvoiceProviderStatus.ReadyToIssue)]
    [InlineData(InvoiceProviderStatus.Previewed)]
    public void Safe_pre_provider_states_allow_route_change(
        InvoiceProviderStatus status)
    {
        var invoice = Invoice(status);

        Assert.True(
            InvoiceIssuanceStatePolicy.CanChangeRoute(invoice));

        Assert.True(
            InvoiceIssuanceStatePolicy
                .CanEditBuyerInformation(invoice));
    }

    [Theory]
    [InlineData(InvoiceProviderStatus.Issuing)]
    [InlineData(InvoiceProviderStatus.IssuedWaitingNumber)]
    public void Provider_uncertain_states_block_route_change(
        InvoiceProviderStatus status)
    {
        var invoice = Invoice(status);

        Assert.False(
            InvoiceIssuanceStatePolicy.CanChangeRoute(invoice));

        Assert.False(
            InvoiceIssuanceStatePolicy
                .CanEditBuyerInformation(invoice));
    }

    [Theory]
    [InlineData(InvoiceProviderStatus.Issued)]
    [InlineData(InvoiceProviderStatus.PdfDownloaded)]
    [InlineData(InvoiceProviderStatus.ZipDownloaded)]
    [InlineData(InvoiceProviderStatus.EmailSent)]
    public void Issued_like_states_block_route_change(
        InvoiceProviderStatus status)
    {
        var invoice = Invoice(status);

        Assert.True(
            InvoiceIssuanceStatePolicy.IsIssuedLike(invoice));

        Assert.False(
            InvoiceIssuanceStatePolicy.CanChangeRoute(invoice));
    }

    [Fact]
    public void Provider_invoice_number_is_issued_like_even_if_status_is_draft()
    {
        var invoice = Invoice(
            InvoiceProviderStatus.LocalDraft);

        invoice.ProviderInvoiceNo = "0000123";

        Assert.True(
            InvoiceIssuanceStatePolicy.IsIssuedLike(invoice));

        Assert.False(
            InvoiceIssuanceStatePolicy.CanChangeRoute(invoice));
    }

    [Fact]
    public void Locked_invoice_blocks_route_change()
    {
        var invoice = Invoice(
            InvoiceProviderStatus.LocalDraft);

        invoice.IsLocked = true;

        Assert.False(
            InvoiceIssuanceStatePolicy.CanChangeRoute(invoice));
    }

    [Theory]
    [InlineData("TIMEOUT")]
    [InlineData("UNKNOWN_RESULT")]
    [InlineData("HTTP_500")]
    [InlineData("VIETTEL_SERVER_500")]
    [InlineData("HTTP_503")]
    public void Uncertain_error_codes_block_route_change(
        string errorCode)
    {
        var invoice = Invoice(
            InvoiceProviderStatus.LocalDraft);

        invoice.LastErrorCode = errorCode;

        Assert.True(
            InvoiceIssuanceStatePolicy
                .IsProviderUncertain(invoice));

        Assert.False(
            InvoiceIssuanceStatePolicy
                .CanChangeRoute(invoice));
    }

    [Fact]
    public void Issue_failed_remains_blocked_until_recovered_to_safe_state()
    {
        var invoice = Invoice(
            InvoiceProviderStatus.IssueFailed);

        Assert.False(
            InvoiceIssuanceStatePolicy.CanChangeRoute(invoice));
    }

    private static InvoiceHead Invoice(
        InvoiceProviderStatus status)
        => new()
        {
            Id = 1,
            StoreId = 1,
            OrderId = 10,
            ProviderStatus = status,
            GrandTotal = 100_000m
        };
}