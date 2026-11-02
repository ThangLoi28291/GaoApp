using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.POSShiftDashboards;

public static class POSShiftPaymentChannels
{
    public static readonly string[] NonCash = ["BankManual", "BankAutomatic", "Card", "EWallet", "Other", "BankUnverified"];
    public static string Label(string channel) => channel switch
    {
        "Cash" => "Tiền mặt", "BankManual" => "CK thủ công", "BankAutomatic" => "CK tự động",
        "Card" => "Thẻ", "EWallet" => "Ví điện tử", "BankUnverified" => "CK cần kiểm tra", _ => "Khác"
    };
    public static string Resolve(PaymentMethod method, bool verified, bool automatedEvidence, bool offlineManual)
        => method == PaymentMethod.BankTransfer
            ? verified ? "BankAutomatic" : offlineManual || !automatedEvidence ? "BankManual" : "BankUnverified"
            : method == PaymentMethod.Cash ? "Cash" : method == PaymentMethod.Card ? "Card"
            : method == PaymentMethod.EWallet ? "EWallet" : "Other";
}

public sealed class POSShiftPaymentChannelTotalDto
{
    public string Channel { get; set; } = "";
    public string Label => POSShiftPaymentChannels.Label(Channel);
    public int PaymentCount { get; set; }
    public decimal RecordedAmount { get; set; }
    public decimal SalesAmount { get; set; }
}

public sealed class POSShiftPaymentBreakdownDto
{
    public decimal ShiftNonCashSalesTotal { get; set; }
    public decimal DetailNonCashSalesTotal => Channels.Sum(x => x.SalesAmount);
    public decimal Difference => ShiftNonCashSalesTotal - DetailNonCashSalesTotal;
    public List<POSShiftPaymentChannelTotalDto> Channels { get; set; } = POSShiftPaymentChannels.NonCash
        .Select(x => new POSShiftPaymentChannelTotalDto { Channel = x }).ToList();

    public static POSShiftPaymentBreakdownDto Combine(IEnumerable<POSShiftPaymentBreakdownDto> source)
    {
        var items = source.ToArray();
        var result = new POSShiftPaymentBreakdownDto { ShiftNonCashSalesTotal = items.Sum(x => x.ShiftNonCashSalesTotal) };
        foreach (var row in result.Channels)
        {
            var rows = items.SelectMany(x => x.Channels).Where(x => x.Channel == row.Channel).ToArray();
            row.PaymentCount = rows.Sum(x => x.PaymentCount);
            row.RecordedAmount = rows.Sum(x => x.RecordedAmount);
            row.SalesAmount = rows.Sum(x => x.SalesAmount);
        }
        return result;
    }
}
