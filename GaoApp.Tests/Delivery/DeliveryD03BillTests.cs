using System.Drawing;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Domain.Delivery;
using Microsoft.EntityFrameworkCore;
using ZXing;
using static GaoApp.Tests.Delivery.DeliveryD03Support;

namespace GaoApp.Tests.Delivery;

[SupportedOSPlatform("windows"), Collection("DeliveryD03"), Trait("Category","DeliveryD03")]
public sealed class DeliveryD03BillTests(DeliveryD02Fixture fixture)
{
    [Theory]
    [InlineData("1.2345", "1000.25", "1,2345", "1.000,25", "1.235", 1235)]
    [InlineData("0.0001", "10000.25", "0,0001", "10.000,25", "1", 1)]
    public async Task Bill_preserves_four_decimal_quantity_and_two_decimal_unit_price_with_whole_dong_totals(
        string quantityText, string priceText, string expectedQuantity, string expectedPrice, string expectedMoney, int total)
    {
        using var c = await fixture.CaseAsync(false);
        var quantity = decimal.Parse(quantityText, CultureInfo.InvariantCulture);
        var unitPrice = decimal.Parse(priceText, CultureInfo.InvariantCulture);
        await using (var db = c.Context())
        {
            var cart = await db.Orders.Include(x => x.Lines).SingleAsync(x => x.Id == c.CartId);
            var line = Assert.Single(cart.Lines);
            line.ItemName = "Hàng <script>alert('line')</script>";
            line.Quantity = quantity; line.BaseQuantity = quantity * line.Multiplier; line.UnitPrice = unitPrice;
            line.OriginalUnitPrice = unitPrice; line.LineTotal = DeliveryValues.PricedAmount(quantity, unitPrice);
            cart.Subtotal = line.LineTotal; cart.GrandTotal = line.LineTotal; cart.BalanceDue = line.LineTotal;
            await db.SaveChangesAsync();
        }
        var result = await Create(c, await Request(c)); // Fresh server rowversion and fingerprint after the source edits.
        var savedLine = Assert.Single(result.Delivery.Lines);
        Assert.Equal(quantity, savedLine.OrderedQuantity); Assert.Equal(unitPrice, savedLine.UnitPrice);
        Assert.Equal(total, result.Delivery.QuotedTotal); Assert.Equal(total, savedLine.Net);
        var html = await c.Client.Http.GetStringAsync(result.BillUrl);
        Assert.Equal(html, await c.Client.Http.GetStringAsync(result.BillUrl));
        var text = WebUtility.HtmlDecode(html);
        Assert.Contains($"<td>{expectedQuantity}</td><td>{expectedPrice}</td><td>{expectedMoney}</td>", text);
        Assert.Contains($"<strong>{expectedMoney} đ</strong>", text);
        Assert.DoesNotContain("<script>alert('line')</script>", html); Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("data:image/png;base64,", html); Assert.Contains(result.Delivery.Code, text);
        Assert.Contains("?key=" + result.Delivery.LookupToken, text);
        var lookup = await c.Client.Http.GetFromJsonAsync<DeliveryDetailDto>("/admin/api/deliveries/lookup?key=" + result.Delivery.Code);
        Assert.Equal(result.Delivery.Id, lookup!.Id); Assert.Equal(quantity, Assert.Single(lookup.Lines).OrderedQuantity);
        Assert.Equal(unitPrice, Assert.Single(lookup.Lines).UnitPrice); Assert.Equal(total, lookup.QuotedTotal);
    }

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
        Assert.Equal("?key="+result.Delivery.LookupToken+"&revision="+result.Delivery.Revision,url.Query);
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
