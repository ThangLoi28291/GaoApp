using GaoApp.Application.Common.Helpers;

namespace GaoApp.Tests.Configuration;

public class VietnamTimeTests
{
    [Theory]
    [InlineData(DateTimeKind.Utc)] [InlineData(DateTimeKind.Unspecified)]
    public void Sql_and_utc_instants_use_Vietnam_time_and_cross_the_day_boundary(DateTimeKind kind)
    {
        var utc = new DateTime(2026, 10, 10, 18, 26, 8, kind);
        Assert.Equal(new DateTime(2026, 10, 11, 1, 26, 8), utc.ToVietnamTime());
        Assert.Equal(DateTimeKind.Unspecified, utc.ToVietnamTime().Kind);
    }

    [Fact]
    public void Calendar_day_filters_start_at_Vietnam_midnight()
    {
        var start = VietnamTime.ToUtc(new DateTime(2026, 10, 11));
        Assert.Equal(new DateTime(2026, 10, 10, 17, 0, 0, DateTimeKind.Utc), start);
        Assert.Equal(new DateTime(2026, 10, 11), start.ToVietnamTime());
    }

    [Fact]
    public void Already_offset_instants_are_converted_once()
    {
        var local = new DateTimeOffset(2026, 10, 11, 13, 26, 8, VietnamTime.Offset);
        Assert.Equal(local, local.ToVietnamTime());
        Assert.Equal(local, local.ToUniversalTime().ToVietnamTime());
        Assert.Equal(local.DateTime, local.UtcDateTime.ToLocalTime().ToVietnamTime());
    }

    [Fact]
    public void Invoice_waiting_days_follow_Vietnam_calendar_midnight()
    {
        var document = new GaoApp.Application.DTOs.Inventory.StockDocumentListItemDto
        {
            WaitForInputInvoice = true,
            ApprovedAtUtc = VietnamTime.ToUtc(VietnamTime.Today).AddSeconds(-1)
        };
        Assert.Equal(1, document.InvoiceWaitingDays);
        document.ApprovedAtUtc = VietnamTime.ToUtc(VietnamTime.Today).AddSeconds(1);
        Assert.Equal(0, document.InvoiceWaitingDays);
    }
}
