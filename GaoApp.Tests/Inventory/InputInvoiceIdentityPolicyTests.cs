using FluentAssertions;
using GaoApp.Application.Common.Helpers;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;

namespace GaoApp.Tests.Inventory;

public sealed class InputInvoiceIdentityPolicyTests
{
    [Fact]
    public void Identity_should_normalize_formatting_without_changing_display_values()
    {
        var invoice = new InputInvoiceHead
        {
            SellerTaxCode = " 031.277.0607-001 ",
            InvoiceSeries = " c26 taa ",
            InvoiceNumber = " 00 0123 ",
            InvoiceDate = new DateTime(2026, 8, 17, 23, 59, 58)
        };

        InputInvoiceIdentityPolicy.ApplyRequiredIdentity(invoice);

        invoice.SellerTaxCode.Should().Be("031.277.0607-001");
        invoice.InvoiceSeries.Should().Be("c26 taa");
        invoice.InvoiceNumber.Should().Be("00 0123");
        invoice.NormalizedSellerTaxCode.Should().Be("0312770607001");
        invoice.NormalizedInvoiceSeries.Should().Be("C26TAA");
        invoice.NormalizedInvoiceNumber.Should().Be("000123");
        invoice.InvoiceIdentityDate.Should().Be(new DateTime(2026, 8, 17));
    }

    [Theory]
    [InlineData(null, "C26TAA", "123", "XML thiếu mã số thuế người bán để định danh hóa đơn.")]
    [InlineData("0312770607", null, "123", "XML thiếu ký hiệu hóa đơn để định danh hóa đơn.")]
    [InlineData("0312770607", "C26TAA", null, "XML thiếu số hóa đơn để định danh hóa đơn.")]
    public void Missing_text_identity_component_should_fail_closed(
        string? sellerTaxCode,
        string? invoiceSeries,
        string? invoiceNumber,
        string expectedMessage)
    {
        var invoice = new InputInvoiceHead
        {
            SellerTaxCode = sellerTaxCode,
            InvoiceSeries = invoiceSeries,
            InvoiceNumber = invoiceNumber,
            InvoiceDate = new DateTime(2026, 8, 17)
        };

        var action = () =>
            InputInvoiceIdentityPolicy.ApplyRequiredIdentity(invoice);

        action.Should().Throw<BusinessRuleException>()
            .Which.SafeMessage.Should().Be(expectedMessage);
    }

    [Fact]
    public void Missing_invoice_date_should_fail_closed()
    {
        var invoice = new InputInvoiceHead
        {
            SellerTaxCode = "0312770607",
            InvoiceSeries = "C26TAA",
            InvoiceNumber = "123"
        };

        var action = () =>
            InputInvoiceIdentityPolicy.ApplyRequiredIdentity(invoice);

        action.Should().Throw<BusinessRuleException>()
            .Which.SafeMessage.Should().Be(
                "XML thiếu ngày lập hóa đơn hợp lệ để định danh hóa đơn.");
    }
    [Fact]
    public void Tax_code_normalization_should_remove_only_approved_separators()
    {
        var normalized = InputInvoiceIdentityPolicy.NormalizeTaxCode(
            " 031.277-0607\t\r\n/001 ");

        normalized.Should().Be("0312770607/001");
    }

    [Theory]
    [InlineData(" 031.277-0607\t\r\n/001 ", "0312770607/001")]
    [InlineData("000-001", "000001")]
    [InlineData("đvt-01", "ĐVT01")]
    [InlineData(" . - \t\r\n ", null)]
    public void Shared_tax_code_normalizer_should_preserve_c1_output(
        string input,
        string? expected)
    {
        TaxCodeIdentityNormalizer.Normalize(input).Should().Be(expected);
        InputInvoiceIdentityPolicy.NormalizeTaxCode(input).Should().Be(expected);
    }

    [Fact]
    public void Identity_token_normalization_should_preserve_leading_zeros_and_punctuation()
    {
        var normalized = InputInvoiceIdentityPolicy.NormalizeIdentityToken(
            " 00 01/23-A ");

        normalized.Should().Be("0001/23-A");
    }

    [Fact]
    public void Different_invoice_number_or_date_should_remain_different_identity()
    {
        var first = new InputInvoiceHead
        {
            SellerTaxCode = "0312770607",
            InvoiceSeries = "C26TAA",
            InvoiceNumber = "000123",
            InvoiceDate = new DateTime(2026, 8, 17)
        };

        var differentNumber = new InputInvoiceHead
        {
            SellerTaxCode = "0312770607",
            InvoiceSeries = "C26TAA",
            InvoiceNumber = "000124",
            InvoiceDate = new DateTime(2026, 8, 17)
        };

        var differentDate = new InputInvoiceHead
        {
            SellerTaxCode = "0312770607",
            InvoiceSeries = "C26TAA",
            InvoiceNumber = "000123",
            InvoiceDate = new DateTime(2026, 8, 18)
        };

        InputInvoiceIdentityPolicy.ApplyRequiredIdentity(first);
        InputInvoiceIdentityPolicy.ApplyRequiredIdentity(differentNumber);
        InputInvoiceIdentityPolicy.ApplyRequiredIdentity(differentDate);

        differentNumber.NormalizedInvoiceNumber
            .Should().NotBe(first.NormalizedInvoiceNumber);

        differentDate.InvoiceIdentityDate
            .Should().NotBe(first.InvoiceIdentityDate);
    }
}
