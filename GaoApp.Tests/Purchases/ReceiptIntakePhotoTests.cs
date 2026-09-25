using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Services.Purchases;
using Xunit;

namespace GaoApp.Tests.Purchases;

public class ReceiptIntakePhotoTests
{
    [Fact]
    public void Empty_photo_is_optional() => Assert.Null(ReceiptIntakePhoto.Parse(null));

    [Theory]
    [InlineData("https://example.org/photo.jpg")]
    [InlineData("data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=")]
    [InlineData("data:image/jpeg;base64,not-base64")]
    [InlineData("data:image/jpeg;base64,PGh0bWw+PC9odG1sPg==")]
    public void Rejects_urls_active_content_and_non_jpeg(string input)
        => Assert.Throws<BusinessRuleException>(() => ReceiptIntakePhoto.Parse(input));

    [Fact]
    public void Rejects_oversized_payload_before_decoding()
        => Assert.Throws<BusinessRuleException>(() => ReceiptIntakePhoto.Parse("data:image/jpeg;base64," + new string('A', 360000)));

    [Fact]
    public void Rejects_unbounded_dimensions_and_truncated_segments()
    {
        byte[] oversized = [0xff,0xd8,0xff,0xc0,0,11,8,0xff,0xff,0xff,0xff,1,1,0x11,0,0xff,0xda,0,2,0xff,0xd9];
        Assert.Throws<BusinessRuleException>(() => ReceiptIntakePhoto.Parse("data:image/jpeg;base64,"+Convert.ToBase64String(oversized)));
        oversized[4]=0xff;oversized[5]=0xff;
        Assert.Throws<BusinessRuleException>(() => ReceiptIntakePhoto.Parse("data:image/jpeg;base64,"+Convert.ToBase64String(oversized)));
    }
}
