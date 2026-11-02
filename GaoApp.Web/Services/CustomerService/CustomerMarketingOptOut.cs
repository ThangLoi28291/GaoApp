namespace GaoApp.Web.Services.CustomerService;

/// <summary>Exact commands only; normal conversation text must never disable marketing by substring.</summary>
public static class CustomerMarketingOptOut
{
    public static bool IsOptOut(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;
        var value = string.Join(' ', message.Trim().TrimEnd('.', '!', '?', ':', ';', ',')
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
        return value is "STOP" or "UNSUBSCRIBE" or "NOADS" or "HUY" or "HỦY" or "NGUNG" or "NGỪNG" or "DUNG" or "DỪNG" or
            "TỪ CHỐI" or "TU CHOI" or "HUY QUANG CAO" or "HỦY QUẢNG CÁO" or "NGỪNG QUẢNG CÁO" or "NGUNG QUANG CAO";
    }
}
