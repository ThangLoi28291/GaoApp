using GaoApp.Application.Common.Exceptions;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Services.Invoices;

// Shares the invoice arithmetic; a gross-inclusive service never increases the quoted customer total.
public static class InvoiceNativeServiceAmounts
{
    public static (decimal Net, decimal Tax) Inclusive(decimal gross, decimal rate)
    {
        if (gross <= 0 || gross >= 10000000000000000m || gross != decimal.Truncate(gross) || rate < 0 || rate > 100 || decimal.Round(rate, 2) != rate)
            throw new BusinessRuleException("Phí hoặc thuế dịch vụ không hợp lệ.");
        var net = decimal.Round(gross / (1m + rate / 100m), 2, MidpointRounding.AwayFromZero);
        foreach (var candidate in new[] { net, net - 0.01m, net + 0.01m })
        {
            var detail = new InvoiceDetail { Quantity = 1, UnitPrice = candidate, VatRate = rate };
            InvoiceAmountCalculator.RecalculateDetail(detail);
            if (detail.TotalAmount == gross && candidate >= 0) return (detail.Amount, detail.VatAmount);
        }
        throw new BusinessRuleException("Phí đã thu chưa biểu diễn chính xác theo cách tính hóa đơn với thuế này. Chưa ghi bán; kiểm tra cấu hình phí/thuế.");
    }
    public static void Recalculate(InvoiceHead head) => InvoiceAmountCalculator.RecalculateHead(head);
}
