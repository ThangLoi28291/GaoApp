using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Services.Purchases;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Inventory;

public partial class StockDocumentService
{
    public async Task<ReceiptPriceDraftResult> SavePriceDraftAsync(int documentId, SaveReceiptPriceDraftRequest request, CancellationToken ct = default)
    {
        await _stockDocumentRepository.BeginTransactionAsync(ct);
        try
        {
            var document = await _stockDocumentRepository.GetForConfirmAsync(documentId, ct)
                ?? throw new BusinessRuleException("Không tìm thấy phiếu nhập.");
            if (document.Type != StockDocumentType.Receipt || document.Status != StockDocumentStatus.PendingApproval)
                throw new BusinessRuleException("Chỉ lưu nháp giá cho phiếu nhập đang chờ duyệt. Vui lòng tải lại để kiểm tra trạng thái phiếu.");
            EnsureRowVersion(document.RowVersion, request.RowVersion);
            if (request.Lines is null || request.Lines.Count is < 1 or > 500 ||
                request.Lines.Select(x => x.StockDocumentLineId).Distinct().Count() != request.Lines.Count)
                throw new BusinessRuleException("Danh sách giá nháp không hợp lệ.");
            var lines = document.Lines.Where(x => !x.IsDeleted).ToDictionary(x => x.Id);
            foreach (var input in request.Lines)
            {
                if (!lines.ContainsKey(input.StockDocumentLineId))
                    throw new BusinessRuleException("Dòng sản phẩm không còn thuộc phiếu nhập này. Vui lòng tải lại phiếu.");
                if (input.UnitPriceBeforeVat <= 0)
                    throw new BusinessRuleException("Giá nhập nháp phải lớn hơn 0.");
                EnsureRowVersion(lines[input.StockDocumentLineId].RowVersion, input.RowVersion);
                EnsureStoredMoney(input.UnitPriceBeforeVat, "Giá nhập nháp");
            }
            foreach (var input in request.Lines)
            {
                var line = lines[input.StockDocumentLineId];
                // Re-entering the same display price must preserve an exact allocated amount.
                if (line.UnitPriceBeforeVat == input.UnitPriceBeforeVat) continue;
                var amounts = PurchasePricingPolicy.CalculateLineFromBeforeVat(line.Quantity, input.UnitPriceBeforeVat, document.HasVat, line.TaxRate);
                EnsureStoredMoney(amounts.LineTotalAfterVat, "Thành tiền nháp");
                line.UnitPriceBeforeVat = amounts.UnitPriceBeforeVat;
                line.UnitCost = amounts.UnitPriceBeforeVat;
                line.UnitPriceAfterVat = amounts.UnitPriceAfterVat;
                line.TaxRate = amounts.TaxRate;
                line.VatAmount = amounts.VatAmount;
                line.LineTotal = amounts.LineTotalAfterVat;
            }
            document.TotalAmount = PurchasePricingPolicy.RoundMoney(lines.Values.Sum(x => x.LineTotal));
            document.VatAmount = document.HasVat ? PurchasePricingPolicy.RoundMoney(lines.Values.Sum(x => x.VatAmount)) : 0m;
            document.SubtotalBeforeVat = document.TotalAmount - document.VatAmount;
            EnsureStoredMoney(document.TotalAmount, "Tổng tiền nháp");
            EnsureStoredMoney(document.SubtotalBeforeVat, "Tổng trước VAT nháp");
            // Force the receipt concurrency token to change even when totals are unchanged.
            document.UpdatedAtUtc = DateTime.UtcNow;
            await InvalidateReconciliationAsync(document, "Giá nhập nháp đã thay đổi; cần đối chiếu lại trước khi duyệt.", ct);
            await _stockDocumentRepository.SaveChangesAsync(ct);
            await _stockDocumentRepository.CommitTransactionAsync(ct);
            return new(Convert.ToBase64String(document.RowVersion), document.UpdatedAtUtc ?? DateTime.UtcNow,
                request.Lines.ToDictionary(x => x.StockDocumentLineId, x => Convert.ToBase64String(lines[x.StockDocumentLineId].RowVersion)),
                request.Lines.ToDictionary(x => x.StockDocumentLineId, x => lines[x.StockDocumentLineId].LineTotal - lines[x.StockDocumentLineId].VatAmount));
        }
        catch
        {
            await _stockDocumentRepository.RollbackTransactionAsync(ct);
            throw;
        }
    }
}
