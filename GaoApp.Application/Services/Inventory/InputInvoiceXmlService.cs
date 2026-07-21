using GaoApp.Application.DTOs.Inventory.InputInvoices;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace GaoApp.Application.Services.Inventory;

public sealed class InputInvoiceXmlService : IInputInvoiceXmlService
{
    private readonly IInputInvoiceRepository _repository;

    public InputInvoiceXmlService(IInputInvoiceRepository repository)
    {
        _repository = repository;
    }

    public async Task<InputInvoiceXmlUploadResultDto> UploadXmlAsync(
        int storeId,
        UploadInputInvoiceXmlRequest request,
        CancellationToken ct = default)
    {
        if (storeId <= 0)
            throw new InvalidOperationException("StoreId không hợp lệ.");

        if (request.StockDocumentId <= 0)
            throw new InvalidOperationException("Phiếu nhập không hợp lệ.");

        if (request.FileBytes == null || request.FileBytes.Length == 0)
            throw new InvalidOperationException("File XML rỗng.");

        var document = await _repository.GetStockDocumentWithLinesAsync(
            storeId,
            request.StockDocumentId,
            ct);

        if (document == null)
            throw new InvalidOperationException("Không tìm thấy phiếu nhập kho.");

        var xmlHash = ComputeSha256(request.FileBytes);

        var existed = await _repository.GetByXmlHashAsync(storeId, xmlHash, ct);
        var isExisting = existed != null;

        InputInvoiceHead head;

        if (existed != null)
        {
            head = existed;
        }
        else
        {
            head = ParseXmlToEntity(request.FileBytes);
            head.StoreId = storeId;
            head.OriginalFileName = request.OriginalFileName;
            head.XmlHash = xmlHash;

            await _repository.AddInputInvoiceAsync(head, ct);
            await _repository.SaveChangesAsync(ct);
        }

        var hasMap = await _repository.ExistsStockDocumentInvoiceMapAsync(
            storeId,
            request.StockDocumentId,
            head.Id,
            ct);

        if (!hasMap)
        {
            await _repository.AddStockDocumentInvoiceMapAsync(new StockDocumentInputInvoiceMap
            {
                StoreId = storeId,
                StockDocumentId = request.StockDocumentId,
                InputInvoiceHeadId = head.Id,
                Note = $"Upload XML: {request.OriginalFileName}"
            }, ct);
        }

        // Seed map dòng nhập.
        // Mặc định UseInputInvoice=false để user tự chọn hoặc chọn all ở UI bước sau.
        await _repository.AddMissingLineMapsAsync(
            storeId,
            request.StockDocumentId,
            ct);

        await _repository.SaveChangesAsync(ct);

        return new InputInvoiceXmlUploadResultDto
        {
            InputInvoiceHeadId = head.Id,
            StockDocumentId = request.StockDocumentId,
            InvoiceTemplateCode = head.InvoiceTemplateCode,
            InvoiceSeries = head.InvoiceSeries,
            InvoiceNumber = head.InvoiceNumber,
            InvoiceDate = head.InvoiceDate,
            SellerTaxCode = head.SellerTaxCode,
            SellerName = head.SellerName,
            TotalBeforeTax = head.TotalBeforeTax,
            TotalTaxAmount = head.TotalTaxAmount,
            TotalPaymentAmount = head.TotalPaymentAmount,
            DetailCount = head.Details?.Count ?? 0,
            IsExistingInvoice = isExisting
        };
    }
    public async Task<List<InputInvoiceHeadDto>> GetInvoicesByStockDocumentAsync(
    int storeId,
    int stockDocumentId,
    CancellationToken ct = default)
    {
        if (storeId <= 0)
            throw new InvalidOperationException("StoreId không hợp lệ.");

        if (stockDocumentId <= 0)
            throw new InvalidOperationException("Phiếu nhập không hợp lệ.");

        var invoices = await _repository.GetByStockDocumentAsync(
            storeId,
            stockDocumentId,
            ct);

        return invoices.Select(x => new InputInvoiceHeadDto
        {
            Id = x.Id,
            InvoiceTemplateCode = x.InvoiceTemplateCode,
            InvoiceSeries = x.InvoiceSeries,
            InvoiceNumber = x.InvoiceNumber,
            InvoiceDate = x.InvoiceDate,
            SellerTaxCode = x.SellerTaxCode,
            SellerName = x.SellerName,
            TotalBeforeTax = x.TotalBeforeTax,
            TotalTaxAmount = x.TotalTaxAmount,
            TotalPaymentAmount = x.TotalPaymentAmount,
            DetailCount = x.Details?.Count ?? 0,

            Details = (x.Details ?? new List<GaoApp.Domain.Entities.InputInvoiceDetail>())
                .OrderBy(d => d.LineNo)
                .Select(d => new InputInvoiceDetailDto
                {
                    Id = d.Id,
                    LineNo = d.LineNo,
                    ItemName = d.ItemName,
                    UnitName = d.UnitName,
                    Quantity = d.Quantity,
                    UnitPrice = d.UnitPrice,
                    LineAmount = d.LineAmount,
                    VatRate = d.VatRate,
                    VatAmount = d.VatAmount
                })
                .ToList()
        }).ToList();
    }
    public async Task<List<StockDocumentLineInputInvoiceMapDto>> GetLineMapsAsync(
    int storeId,
    int stockDocumentId,
    CancellationToken ct = default)
    {
        await _repository.AddMissingLineMapsAsync(storeId, stockDocumentId, ct);
        await _repository.SaveChangesAsync(ct);

        var maps = await _repository.GetLineMapsByStockDocumentAsync(
            storeId,
            stockDocumentId,
            ct);

        return maps.Select(x => new StockDocumentLineInputInvoiceMapDto
        {
            StockDocumentId = x.StockDocumentId,
            StockDocumentLineId = x.StockDocumentLineId,
            UseInputInvoice = x.UseInputInvoice,
            InputInvoiceDetailId = x.InputInvoiceDetailId,
            MatchStatus = x.MatchStatus,
            QuantityDifference = x.QuantityDifference,
            AmountDifference = x.AmountDifference,

            XmlItemName = x.InputInvoiceDetail?.ItemName,
            XmlUnitName = x.InputInvoiceDetail?.UnitName,
            XmlQuantity = x.InputInvoiceDetail?.Quantity,
            XmlLineAmount = x.InputInvoiceDetail?.LineAmount
        }).ToList();
    }

    public async Task UpdateLineMapAsync(
        int storeId,
        int stockDocumentId,
        UpdateStockDocumentLineInputInvoiceMapRequest request,
        CancellationToken ct = default)
    {
        if (storeId <= 0)
            throw new InvalidOperationException("StoreId không hợp lệ.");

        if (stockDocumentId <= 0)
            throw new InvalidOperationException("Phiếu nhập không hợp lệ.");

        if (request.StockDocumentLineId <= 0)
            throw new InvalidOperationException("Dòng nhập không hợp lệ.");

        var line = await _repository.GetStockDocumentLineAsync(
            storeId,
            request.StockDocumentLineId,
            ct);

        if (line == null || line.StockDocumentId != stockDocumentId)
            throw new InvalidOperationException("Dòng nhập không thuộc phiếu hiện tại.");

        var map = await _repository.GetLineMapAsync(
            storeId,
            request.StockDocumentLineId,
            ct);

        if (map == null)
        {
            map = new StockDocumentLineInputInvoiceMap
            {
                StoreId = storeId,
                StockDocumentId = stockDocumentId,
                StockDocumentLineId = request.StockDocumentLineId
            };

            // Nếu repository chưa có Add riêng thì có thể thêm method AddLineMapAsync.
            throw new InvalidOperationException("Map dòng chưa được khởi tạo. Hãy reload phiếu hoặc gọi AddMissingLineMapsAsync trước.");
        }

        map.UseInputInvoice = request.UseInputInvoice;

        if (!request.UseInputInvoice)
        {
            map.InputInvoiceDetailId = null;
            map.MatchStatus = InputInvoiceMatchStatus.Excluded;
            map.QuantityDifference = 0;
            map.AmountDifference = 0;
            map.Note = "User chọn dòng này không thuộc hóa đơn XML.";
        }
        else
        {
            // CHỐT PHƯƠNG ÁN 2.6:
            // UseInputInvoice = true là đủ xác nhận dòng nhập này thuộc hóa đơn XML.
            // InputInvoiceDetailId không bắt buộc, chỉ dùng để đối chiếu sâu nếu user chọn.
            if (!request.InputInvoiceDetailId.HasValue || request.InputInvoiceDetailId.Value <= 0)
            {
                map.InputInvoiceDetailId = null;
                map.MatchStatus = InputInvoiceMatchStatus.None;
                map.QuantityDifference = 0;
                map.AmountDifference = 0;
                map.Note = "User chọn dòng này thuộc hóa đơn XML nhưng chưa map chi tiết dòng XML.";
            }
            else
            {
                var xmlLine = await _repository.GetInputInvoiceDetailAsync(
                    request.InputInvoiceDetailId.Value,
                    ct);

                if (xmlLine == null)
                    throw new InvalidOperationException("Không tìm thấy dòng XML.");

                map.InputInvoiceDetailId = xmlLine.Id;

                var quantityDiff = line.Quantity - xmlLine.Quantity;
                var amountDiff = line.LineTotal - xmlLine.LineAmount;

                map.QuantityDifference = quantityDiff;
                map.AmountDifference = amountDiff;
                map.MatchStatus = ResolveMatchStatus(quantityDiff, amountDiff);
                map.Note = null;
            }
        }

        await _repository.SaveChangesAsync(ct);
    }
    public async Task BulkUpdateLineMapsAsync(
    int storeId,
    int stockDocumentId,
    bool useInputInvoice,
    CancellationToken ct = default)
    {
        if (storeId <= 0)
            throw new InvalidOperationException("StoreId không hợp lệ.");

        if (stockDocumentId <= 0)
            throw new InvalidOperationException("Phiếu nhập không hợp lệ.");

        // Đảm bảo mỗi dòng nhập đều có map
        await _repository.AddMissingLineMapsAsync(storeId, stockDocumentId, ct);
        await _repository.SaveChangesAsync(ct);

        var maps = await _repository.GetLineMapsByStockDocumentAsync(
            storeId,
            stockDocumentId,
            ct);

        foreach (var map in maps)
        {
            map.UseInputInvoice = useInputInvoice;

            if (!useInputInvoice)
            {
                map.InputInvoiceDetailId = null;
                map.MatchStatus = InputInvoiceMatchStatus.Excluded;
                map.QuantityDifference = 0;
                map.AmountDifference = 0;
                map.Note = "User chọn bỏ tất cả dòng khỏi hóa đơn XML.";
            }
            else
            {
                // Không bắt buộc map chi tiết dòng XML
                map.InputInvoiceDetailId = map.InputInvoiceDetailId;
                map.MatchStatus = map.InputInvoiceDetailId.HasValue
                    ? map.MatchStatus
                    : InputInvoiceMatchStatus.None;

                map.QuantityDifference = map.InputInvoiceDetailId.HasValue
                    ? map.QuantityDifference
                    : 0;

                map.AmountDifference = map.InputInvoiceDetailId.HasValue
                    ? map.AmountDifference
                    : 0;

                map.Note = map.InputInvoiceDetailId.HasValue
                    ? map.Note
                    : "User chọn tất cả dòng thuộc hóa đơn XML nhưng chưa map chi tiết dòng XML.";
            }
        }

        await _repository.SaveChangesAsync(ct);
    }

    private static InputInvoiceMatchStatus ResolveMatchStatus(decimal quantityDiff, decimal amountDiff)
    {
        var qtyMismatch = Math.Abs(quantityDiff) > 0.0001m;
        var amountMismatch = Math.Abs(amountDiff) > 1m;

        if (qtyMismatch && amountMismatch)
            return InputInvoiceMatchStatus.QuantityAndAmountMismatch;

        if (qtyMismatch)
            return InputInvoiceMatchStatus.QuantityMismatch;

        if (amountMismatch)
            return InputInvoiceMatchStatus.AmountMismatch;

        return InputInvoiceMatchStatus.Matched;
    }

    private static InputInvoiceHead ParseXmlToEntity(byte[] fileBytes)
    {
        var xmlText = Encoding.UTF8.GetString(fileBytes);
        var xdoc = XDocument.Parse(xmlText, LoadOptions.PreserveWhitespace);

        var root = xdoc.Root
            ?? throw new InvalidOperationException("XML không hợp lệ.");

        var ttChung = root.Descendants().FirstOrDefault(x => x.Name.LocalName == "TTChung");
        var nBan = root.Descendants().FirstOrDefault(x => x.Name.LocalName == "NBan");
        var nMua = root.Descendants().FirstOrDefault(x => x.Name.LocalName == "NMua");
        var tToan = root.Descendants().FirstOrDefault(x => x.Name.LocalName == "TToan");

        string? ChildValue(XElement? parent, string localName)
        {
            return parent?
                .Elements()
                .FirstOrDefault(x => x.Name.LocalName == localName)?
                .Value
                ?.Trim();
        }

        var head = new InputInvoiceHead
        {
            InvoiceTemplateCode = ChildValue(ttChung, "KHMSHDon"),
            InvoiceSeries = ChildValue(ttChung, "KHHDon"),
            InvoiceNumber = ChildValue(ttChung, "SHDon"),
            InvoiceDate = ParseDate(ChildValue(ttChung, "NLap")),
            TaxAuthorityCode = ChildValue(ttChung, "MSTTCGP"),

            SellerName = ChildValue(nBan, "Ten"),
            SellerTaxCode = ChildValue(nBan, "MST"),
            SellerAddress = ChildValue(nBan, "DChi"),

            BuyerName = ChildValue(nMua, "Ten"),
            BuyerTaxCode = ChildValue(nMua, "MST"),
            BuyerAddress = ChildValue(nMua, "DChi"),

            TotalBeforeTax = ParseMoney(ChildValue(tToan, "TgTCThue")),
            TotalTaxAmount = ParseMoney(ChildValue(tToan, "TgTThue")),
            TotalPaymentAmount = ParseMoney(ChildValue(tToan, "TgTTTBSo"))
        };

        var lineElements = root
            .Descendants()
            .Where(x => x.Name.LocalName == "HHDVu")
            .ToList();

        foreach (var item in lineElements)
        {
            var lineNo = ParseInt(ChildValue(item, "STT"));
            var itemName = ChildValue(item, "THHDVu");

            if (string.IsNullOrWhiteSpace(itemName))
                continue;

            head.Details.Add(new InputInvoiceDetail
            {
                LineNo = lineNo,
                ItemName = itemName,
                UnitName = ChildValue(item, "DVTinh"),
                Quantity = ParseQuantity(ChildValue(item, "SLuong")),
                UnitPrice = ParseMoney(ChildValue(item, "DGia")),
                LineAmount = ParseMoney(ChildValue(item, "ThTien")),
                VatRate = ChildValue(item, "TSuat"),
                VatAmount = ExtractVatAmount(item)
            });
        }

        if (head.Details.Count == 0)
            throw new InvalidOperationException("XML không có dòng hàng hóa dịch vụ.");

        return head;
    }

    private static decimal ExtractVatAmount(XElement item)
    {
        // Một số XML để VATAmount trong TTKhac/TTin.
        var vatNode = item
            .Descendants()
            .Where(x => x.Name.LocalName == "TTin")
            .FirstOrDefault(x =>
                x.Elements().Any(e => e.Name.LocalName == "TTruong" &&
                                      e.Value.Trim() == "VATAmount"));

        var value = vatNode?
            .Elements()
            .FirstOrDefault(x => x.Name.LocalName == "DLieu")
            ?.Value;

        return ParseMoney(value);
    }

    private static string ComputeSha256(byte[] bytes)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;

        if (DateTime.TryParse(value, new CultureInfo("vi-VN"), DateTimeStyles.None, out d))
            return d;

        return null;
    }

    private static int ParseInt(string? value)
    {
        if (int.TryParse(value, out var n))
            return n;

        return 0;
    }

    private static decimal ParseQuantity(string? value)
    {
        return ParseMoney(value);
    }

    private static decimal ParseMoney(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        value = value.Trim()
            .Replace(",", ".");

        if (decimal.TryParse(
                value,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var d))
        {
            return d;
        }

        return 0;
    }
}
