using GaoApp.Web.Services.CustomerService;

namespace GaoApp.Tests.CustomerService;

public sealed class CustomerMarketingOptOutTests
{
    [Theory]
    [InlineData("STOP")]
    [InlineData(" stop! ")]
    [InlineData("UNSUBSCRIBE")]
    [InlineData("Hủy quảng cáo")]
    [InlineData("NGỪNG")]
    [InlineData("tu   choi")]
    public void Exact_opt_out_commands_are_recognized(string message)
        => Assert.True(CustomerMarketingOptOut.IsOptOut(message));

    [Theory]
    [InlineData("")]
    [InlineData("Đừng stop lại nữa, gửi thông tin giá giúp tôi")]
    [InlineData("Tôi muốn hủy đơn hàng")]
    [InlineData("Xin chào")]
    [InlineData(null)]
    public void Ordinary_or_ambiguous_chat_is_not_an_opt_out(string? message)
        => Assert.False(CustomerMarketingOptOut.IsOptOut(message));
}
