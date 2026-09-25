using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Helpers;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Domain.Entities;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace GaoApp.Application.Services.Inventory;

public sealed class InputInvoiceXmlDocumentParser : IInputInvoiceXmlDocumentParser
{
    private const int MaximumXmlBytes = 10 * 1024 * 1024;
    private const long MaximumXmlCharacters = 12 * 1024 * 1024;

    public InputInvoiceHead Parse(byte[] fileBytes)
    {
        if (fileBytes is null || fileBytes.Length == 0)
            throw new BusinessRuleException("File XML rỗng.");
        if (fileBytes.Length > MaximumXmlBytes)
            throw new BusinessRuleException("File XML vượt quá giới hạn cho phép.");

        XDocument document;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumXmlCharacters,
                IgnoreComments = true
            };
            using var stream = new MemoryStream(fileBytes, writable: false);
            using var reader = XmlReader.Create(stream, settings);
            document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException exception)
        {
            throw new BusinessRuleException($"XML không hợp lệ: {exception.Message}");
        }

        var root = document.Root
            ?? throw new BusinessRuleException("XML không hợp lệ.");
        var common = Descendant(root, "TTChung");
        var seller = Descendant(root, "NBan");
        var buyer = Descendant(root, "NMua");
        var totals = Descendant(root, "TToan");

        var invoice = new InputInvoiceHead
        {
            InvoiceTemplateCode = ChildValue(common, "KHMSHDon"),
            InvoiceSeries = ChildValue(common, "KHHDon"),
            InvoiceNumber = ChildValue(common, "SHDon"),
            InvoiceDate = ParseDate(ChildValue(common, "NLap")),
            TaxAuthorityCode = ChildValue(common, "MSTTCGP"),
            SellerName = ChildValue(seller, "Ten"),
            SellerTaxCode = ChildValue(seller, "MST"),
            SellerAddress = ChildValue(seller, "DChi"),
            BuyerName = ChildValue(buyer, "Ten"),
            BuyerTaxCode = ChildValue(buyer, "MST"),
            BuyerAddress = ChildValue(buyer, "DChi"),
            TotalBeforeTax = ParseMoney(ChildValue(totals, "TgTCThue")),
            TotalTaxAmount = ParseMoney(ChildValue(totals, "TgTThue")),
            TotalPaymentAmount = ParseMoney(ChildValue(totals, "TgTTTBSo"))
        };

        foreach (var item in root.Descendants().Where(x => x.Name.LocalName == "HHDVu"))
        {
            var name = ChildValue(item, "THHDVu");
            if (string.IsNullOrWhiteSpace(name))
                continue;
            var supplierItemCode = ChildValue(item, "MHHDVu");
            var unitName = ChildValue(item, "DVTinh");
            invoice.Details.Add(new InputInvoiceDetail
            {
                LineNo = ParseInt(ChildValue(item, "STT")),
                SupplierItemCode = supplierItemCode,
                NormalizedSupplierItemCode =
                    InputInvoiceItemIdentityNormalizer.NormalizeCode(supplierItemCode),
                ItemName = name,
                NormalizedItemName =
                    InputInvoiceItemIdentityNormalizer.NormalizeText(name),
                UnitName = unitName,
                NormalizedUnitName =
                    InputInvoiceItemIdentityNormalizer.NormalizeText(unitName),
                Quantity = ParseMoney(ChildValue(item, "SLuong")),
                UnitPrice = ParseMoney(ChildValue(item, "DGia")),
                LineAmount = ParseMoney(ChildValue(item, "ThTien")),
                VatRate = ChildValue(item, "TSuat"),
                VatAmount = ExtractVatAmount(item)
            });
        }

        if (invoice.Details.Count == 0)
            throw new BusinessRuleException("XML không có dòng hàng hóa dịch vụ.");

        InputInvoiceIdentityPolicy.ApplyRequiredIdentity(invoice);
        return invoice;
    }

    private static XElement? Descendant(XElement root, string name) =>
        root.Descendants().FirstOrDefault(x => x.Name.LocalName == name);

    private static string? ChildValue(XElement? parent, string name) =>
        parent?.Elements().FirstOrDefault(x => x.Name.LocalName == name)?.Value.Trim();

    private static decimal ExtractVatAmount(XElement item)
    {
        var node = item.Descendants().Where(x => x.Name.LocalName == "TTin")
            .FirstOrDefault(x => x.Elements().Any(e =>
                e.Name.LocalName == "TTruong" && e.Value.Trim() == "VATAmount"));
        return ParseMoney(ChildValue(node, "DLieu"));
    }

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
            return result;
        if (DateTime.TryParse(value, new CultureInfo("vi-VN"), DateTimeStyles.None, out result))
            return result;
        return null;
    }

    private static int ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result : 0;

    private static decimal ParseMoney(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        return decimal.TryParse(value.Trim().Replace(',', '.'), NumberStyles.Any,
            CultureInfo.InvariantCulture, out var result) ? result : 0;
    }
}
