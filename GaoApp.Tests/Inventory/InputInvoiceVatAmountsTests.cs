using System.Text;
using GaoApp.Application.DTOs.Inventory.InputInvoices;
using GaoApp.Application.Services.Inventory;

namespace GaoApp.Tests.Inventory;

public sealed class InputInvoiceVatAmountsTests
{
    [Fact]
    public void Xml_parser_uses_each_line_tax_rate_and_net_amount_after_discount()
    {
        var invoice = Parse("""
            <HHDVu><STT>1</STT><THHDVu>Aquafina</THHDVu><DVTinh>Thùng</DVTinh><SLuong>5</SLuong><DGia>100000</DGia><ThTien>453704</ThTien><TSuat>8%</TSuat></HHDVu>
            <HHDVu><STT>2</STT><THHDVu>Hàng VAT 10%</THHDVu><SLuong>2</SLuong><DGia>50</DGia><ThTien>100</ThTien><TSuat>10%</TSuat></HHDVu>
            """);
        Assert.Equal(36296.32m, invoice.Details.ElementAt(0).VatAmount);
        Assert.Equal(10m, invoice.Details.ElementAt(1).VatAmount);
        var first = View(invoice.Details.ElementAt(0));
        Assert.Equal(490000.32m, first.LineAmountAfterVat);
        Assert.Equal(98000.064m, first.UnitPriceAfterVat);
    }

    [Theory]
    [InlineData("<TThue>7.99</TThue>")]
    [InlineData("<TTKhac><TTin><TTruong>VATAmount</TTruong><DLieu>7.99</DLieu></TTin></TTKhac>")]
    public void Explicit_line_tax_takes_priority_over_rate_calculation(string tax)
    {
        var line = Assert.Single(Parse($"<HHDVu><THHDVu>Hàng</THHDVu><SLuong>1</SLuong><DGia>100</DGia><ThTien>100</ThTien><TSuat>8%</TSuat>{tax}</HHDVu>").Details);
        Assert.Equal(7.99m, line.VatAmount);
        Assert.Equal(107.99m, View(line).LineAmountAfterVat);
    }

    [Theory]
    [InlineData("8%", 108)]
    [InlineData("10%", 110)]
    [InlineData("0%", 100)]
    [InlineData("KCT", 100)]
    [InlineData("KKKNT", 100)]
    public void Previously_stored_xml_without_line_tax_can_project_gross_price(string rate, decimal gross)
    {
        var line = new InputInvoiceDetailDto { LineAmount = 100m, Quantity = 2m, VatRate = rate };
        Assert.Equal(gross, line.LineAmountAfterVat);
        Assert.Equal(gross / 2m, line.UnitPriceAfterVat);
        Assert.Equal(0m, line.VatAmount); // Read-only projection; imported evidence is retained.
    }

    [Theory]
    [InlineData(null)]
    [InlineData("KHAC")]
    [InlineData("150%")]
    public void Unknown_or_invalid_tax_does_not_claim_a_tax_inclusive_price(string? rate)
    {
        var line = new InputInvoiceDetailDto { LineAmount = 100m, Quantity = 2m, VatRate = rate };
        Assert.Null(line.LineAmountAfterVat);
        Assert.Null(line.UnitPriceAfterVat);
    }

    private static InputInvoiceDetailDto View(GaoApp.Domain.Entities.InputInvoiceDetail line)
        => new() { Quantity = line.Quantity, UnitPrice = line.UnitPrice, LineAmount = line.LineAmount,
            VatRate = line.VatRate, VatAmount = line.VatAmount };

    private static GaoApp.Domain.Entities.InputInvoiceHead Parse(string lines)
        => new InputInvoiceXmlDocumentParser().Parse(Encoding.UTF8.GetBytes($"""
            <HDon xmlns="urn:invoice-test"><DLHDon><TTChung><KHMSHDon>1</KHMSHDon><KHHDon>C26VAT</KHHDon><SHDon>1</SHDon><NLap>2026-10-13</NLap></TTChung>
            <NDHDon><NBan><Ten>Nhà cung cấp thử nghiệm</Ten><MST>0312770607</MST></NBan><DSHHDVu>{lines}</DSHHDVu></NDHDon></DLHDon></HDon>
            """));
}
