using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Invoices;

internal static class InvoiceManualDetailValidator
{
    public static Result<bool> ValidateAmountInput(
        InvoiceHead invoiceHead,
        decimal quantity,
        decimal unitPrice,
        decimal vatRate)
    {
        if (quantity == 0)
        {
            return Result<bool>.Failure(
                Error.Validation(
                    "Invoice.QuantityInvalid",
                    "Số lượng không được bằng 0."));
        }

        if (!IsAmountAdjustmentInvoice(invoiceHead) && quantity < 0)
        {
            return Result<bool>.Failure(
                Error.Validation(
                    "Invoice.NegativeQuantityNotAllowed",
                    "Chỉ hóa đơn điều chỉnh tiền mới được nhập số lượng âm."));
        }

        if (unitPrice < 0)
        {
            return Result<bool>.Failure(
                Error.Validation(
                    "Invoice.UnitPriceInvalid",
                    "Đơn giá không được âm. Muốn điều chỉnh giảm thì nhập số lượng âm."));
        }

        if (vatRate < 0 || vatRate > 100)
        {
            return Result<bool>.Failure(
                Error.Validation(
                    "Invoice.VatRateInvalid",
                    "Thuế suất VAT không hợp lệ."));
        }

        return Result<bool>.Success(true);
    }

    private static bool IsAmountAdjustmentInvoice(InvoiceHead invoiceHead)
    {
        return invoiceHead.CorrectionType == InvoiceCorrectionType.AdjustmentAmount;
    }
}