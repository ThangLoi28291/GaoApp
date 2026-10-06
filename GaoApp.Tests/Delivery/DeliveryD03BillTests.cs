using System.Drawing;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Delivery;
using ZXing;
using static GaoApp.Tests.Delivery.DeliveryD03Support;

namespace GaoApp.Tests.Delivery;

[SupportedOSPlatform("windows"), Collection("DeliveryD03"), Trait("Category","DeliveryD03")]
public sealed class DeliveryD03BillTests(DeliveryD02Fixture fixture)
{
    [Fact]
    public async Task Bill_QR_decodes_to_persisted_token_and_reprint_is_stable_A5_non_payment_escaped()
    {
        using var c=await fixture.CaseAsync(false); var request=await Request(c);
        var result=await Create(c,request with {RecipientName="<script>alert('recipient')</script>",Note="<img src=x onerror=alert(1)>"});
        var html=await c.Client.Http.GetStringAsync(result.BillUrl); var reprint=await c.Client.Http.GetStringAsync(result.BillUrl);
        Assert.Equal(html,reprint); var text=WebUtility.HtmlDecode(html); Assert.Contains("CHỜ SOẠN",text); Assert.Contains("CHƯA THU TIỀN",text); Assert.Contains("148 × 210",text);
        Assert.DoesNotContain("<script>alert('recipient')</script>",html); Assert.DoesNotContain("<img src=x onerror",html);
        Assert.Contains("&lt;script&gt;",html); Assert.DoesNotContain("pos.printing",html); Assert.DoesNotContain("qz-tray",html);
        var encoded=Regex.Match(text,"data:image/png;base64,([A-Za-z0-9+/=]+)").Groups[1].Value;
        Assert.NotEmpty(encoded); using var stream=new MemoryStream(Convert.FromBase64String(encoded)); using var bitmap=new Bitmap(stream);
        var pixels=new byte[bitmap.Width*bitmap.Height*3]; var i=0; for(var y=0;y<bitmap.Height;y++) for(var x=0;x<bitmap.Width;x++){var p=bitmap.GetPixel(x,y);pixels[i++]=p.R;pixels[i++]=p.G;pixels[i++]=p.B;}
        var decoded=new BarcodeReaderGeneric{Options=new(){TryHarder=true,PossibleFormats=[BarcodeFormat.QR_CODE]}}.Decode(new RGBLuminanceSource(pixels,bitmap.Width,bitmap.Height,RGBLuminanceSource.BitmapFormat.RGB24));
        Assert.NotNull(decoded); var url=new Uri(decoded.Text); Assert.Equal(c.Account.Store.Host,url.Host); Assert.Equal("/admin/deliveries",url.AbsolutePath);
        Assert.Equal("?key="+result.Delivery.LookupToken,url.Query);
        var detail=await c.Client.Http.GetFromJsonAsync<DeliveryDetailDto>("/admin/api/deliveries/lookup"+url.Query); Assert.Equal(result.Delivery.Id,detail!.Id);
    }
    [Fact]
    public async Task Bill_cannot_be_read_across_store_or_without_delivery_view_permission()
    {
        using var c=await fixture.CaseAsync(false); var result=await Create(c);
        using var other=await fixture.Web.LoginAsync(await fixture.Web.AddAccountAsync(fixture.Web.Stores[1],PermissionCodes.Delivery.View));
        using var cross=await other.Http.GetAsync(result.BillUrl); Assert.Equal(HttpStatusCode.NotFound,cross.StatusCode);
        using var denied=await fixture.Web.LoginAsync(await fixture.Web.AddAccountAsync(c.Account.Store,PermissionCodes.Pos.Order.View));
        using var response=await denied.Http.GetAsync(result.BillUrl); Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
    }
    [Fact]
    public async Task Another_authorized_counter_can_reprint_after_origin_employee_and_terminal_are_retired()
    {
        using var c=await fixture.CaseAsync(false);var result=await Create(c);
        var viewer=await fixture.Web.AddAccountAsync(c.Account.Store with {TerminalId=fixture.Web.Stores[0].TerminalId},PermissionCodes.Delivery.View);
        using var other=await fixture.Web.LoginAsync(viewer);
        await fixture.Web.Database.ExecuteAsync($"UPDATE Users SET IsDeleted=1,IsActive=0 WHERE Id={c.Account.UserId}; UPDATE POSTerminals SET IsDeleted=1,IsActive=0 WHERE Id={c.Account.Store.TerminalId};");
        using var bill=await other.Http.GetAsync(result.BillUrl);Assert.Equal(HttpStatusCode.OK,bill.StatusCode);Assert.Contains(result.Delivery.Code,await bill.Content.ReadAsStringAsync());
    }
}
