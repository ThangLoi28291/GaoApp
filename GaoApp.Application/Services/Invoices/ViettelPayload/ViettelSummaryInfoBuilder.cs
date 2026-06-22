using GaoApp.Application.DTOs.Invoices;

namespace GaoApp.Application.Services.Invoices;

internal static class ViettelSummaryInfoBuilder
{
    public static ViettelSummaryBuildResult Build(List<ViettelItemInfoDto> itemInfo)
    {
        var totalAmountWithoutTax = ViettelPayloadTextHelper.RoundVnd(
            itemInfo.Sum(x => x.ItemTotalAmountWithoutTax));

        var totalTaxAmount = ViettelPayloadTextHelper.RoundVnd(
            itemInfo.Sum(x => x.TaxAmount));

        var totalAmountWithTax = ViettelPayloadTextHelper.RoundVnd(
            itemInfo.Sum(x => x.ItemTotalAmountWithTax));

        var taxBreakdowns = itemInfo
            .GroupBy(x => x.TaxPercentage)
            .OrderBy(x => x.Key)
            .Select(g => new ViettelTaxBreakdownDto
            {
                TaxPercentage = g.Key,
                TaxableAmount = ViettelPayloadTextHelper.RoundVnd(
                    g.Sum(x => x.ItemTotalAmountWithoutTax)),
                TaxAmount = ViettelPayloadTextHelper.RoundVnd(
                    g.Sum(x => x.TaxAmount))
            })
            .ToList();

        var summarizeInfo = new ViettelSummarizeInfoDto
        {
            SumOfTotalLineAmountWithoutTax = totalAmountWithoutTax,
            TotalAmountWithoutTax = totalAmountWithoutTax,
            TotalTaxAmount = totalTaxAmount,
            TotalAmountWithTax = totalAmountWithTax,
            TotalAmountWithTaxInWords = VietnameseMoneyText.ReadMoney(totalAmountWithTax),
            DiscountAmount = 0,
            SettlementDiscountAmount = 0
        };

        return new ViettelSummaryBuildResult
        {
            TotalAmountWithoutTax = totalAmountWithoutTax,
            TotalTaxAmount = totalTaxAmount,
            TotalAmountWithTax = totalAmountWithTax,
            SummarizeInfo = summarizeInfo,
            TaxBreakdowns = taxBreakdowns
        };
    }
}

internal class ViettelSummaryBuildResult
{
    public decimal TotalAmountWithoutTax { get; set; }

    public decimal TotalTaxAmount { get; set; }

    public decimal TotalAmountWithTax { get; set; }

    public ViettelSummarizeInfoDto SummarizeInfo { get; set; } = new();

    public List<ViettelTaxBreakdownDto> TaxBreakdowns { get; set; } = new();
}