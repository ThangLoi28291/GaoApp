using GaoApp.Application.DTOs.Invoices;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Invoices;

internal static class ViettelItemInfoBuilder
{
    public static List<ViettelItemInfoDto> Build(
        List<InvoiceDetailDto> details,
        List<string> warnings,
        InvoiceCorrectionType? correctionType)
    {
        var result = new List<ViettelItemInfoDto>();

        var lineNo = 1;

        var isAdjustmentAmountInvoice =
            correctionType == InvoiceCorrectionType.AdjustmentAmount;

        var isAdjustmentInfoInvoice =
            correctionType == InvoiceCorrectionType.AdjustmentInfo;

        foreach (var detail in details)
        {
            var quantity = detail.Quantity;
            var unitPrice = ViettelPayloadTextHelper.RoundVnd(detail.UnitPrice);
            var amount = ViettelPayloadTextHelper.RoundVnd(detail.Amount);
            var taxRate = detail.VatRate;
            var taxAmount = ViettelPayloadTextHelper.RoundVnd(detail.VatAmount);
            var totalAmount = ViettelPayloadTextHelper.RoundVnd(detail.TotalAmount);

            bool? isIncreaseItem = null;
            int? adjustmentTaxAmount = null;

            if (isAdjustmentAmountInvoice)
            {
                isIncreaseItem = totalAmount >= 0;
                adjustmentTaxAmount = 1;

                if (totalAmount < 0 && quantity > 0 && unitPrice > 0)
                {
                    warnings.Add(
                        $"Dòng #{detail.Id}: hóa đơn điều chỉnh giảm có TotalAmount âm nhưng Quantity và UnitPrice đều dương. Nên nhập Quantity âm hoặc UnitPrice âm để PDF thể hiện đúng điều chỉnh giảm.");
                }

                if (totalAmount > 0 && quantity < 0)
                {
                    warnings.Add(
                        $"Dòng #{detail.Id}: hóa đơn điều chỉnh tăng có TotalAmount dương nhưng Quantity âm. Cần kiểm tra lại dòng điều chỉnh.");
                }

                if (quantity == 0)
                {
                    warnings.Add(
                        $"Dòng #{detail.Id}: hóa đơn điều chỉnh tiền có số lượng bằng 0.");
                }
            }
            else if (isAdjustmentInfoInvoice)
            {
                // Điều chỉnh thông tin: không phát sinh tiền.
                quantity = 0;
                unitPrice = 0;
                amount = 0;
                taxAmount = 0;
                totalAmount = 0;
            }

            if (!isAdjustmentInfoInvoice)
            {
                var expectedAmount = ViettelPayloadTextHelper.RoundVnd(quantity * unitPrice);

                if (amount != expectedAmount)
                {
                    warnings.Add(
                        $"Dòng #{detail.Id}: Amount ({amount:N0}) khác Quantity x UnitPrice ({expectedAmount:N0}).");
                }

                if (totalAmount != amount + taxAmount)
                {
                    warnings.Add(
                        $"Dòng #{detail.Id}: TotalAmount ({totalAmount:N0}) khác Amount + VAT ({(amount + taxAmount):N0}).");
                }
            }

            result.Add(new ViettelItemInfoDto
            {
                LineNumber = lineNo++,
                Selection = 1,

                ItemCode = detail.ProductVariantId.HasValue
                    ? detail.ProductVariantId.Value.ToString()
                    : null,

                ItemName = ViettelPayloadTextHelper.TrimMax(detail.ItemName, 500),

                UnitName = string.IsNullOrWhiteSpace(detail.UnitName)
                    ? null
                    : ViettelPayloadTextHelper.TrimMax(detail.UnitName, 300),

                UnitPrice = unitPrice,
                Quantity = quantity,

                ItemTotalAmountWithoutTax = amount,
                ItemTotalAmountWithTax = totalAmount,
                ItemTotalAmountAfterDiscount = amount,

                TaxPercentage = taxRate,
                TaxAmount = taxAmount,

                Discount = 0,
                ItemDiscount = 0,

                ItemNote = string.IsNullOrWhiteSpace(detail.Note)
                    ? null
                    : ViettelPayloadTextHelper.TrimMax(detail.Note, 500),

                BatchNo = "",
                ExpDate = "",

                IsIncreaseItem = isIncreaseItem,
                AdjustmentTaxAmount = adjustmentTaxAmount
            });
        }

        return result;
    }
}