using System.Net;
using System.Net.Http.Json;
using GaoApp.Application.DTOs.Delivery;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Delivery;

[CollectionDefinition("DeliveryD03", DisableParallelization = true)]
public sealed class DeliveryD03Collection : ICollectionFixture<DeliveryD02Fixture> { }

internal static class DeliveryD03Support
{
    internal const string CreatePath = "/admin/api/deliveries/from-current-cart";
    internal static async Task<DeliveryPosCreateRequest> Request(DeliveryD02Case c)
    {
        var snapshot = await c.Client.Http.GetFromJsonAsync<DeliveryCartSnapshot>("/admin/api/deliveries/current-cart");
        Assert.NotNull(snapshot);
        return new(Guid.NewGuid(), snapshot.SourceCartId, snapshot.Version, snapshot.Fingerprint, "Người nhận D03", "0901234567", "12 Đường thử nghiệm", "Gọi trước khi giao");
    }
    internal static async Task<DeliveryPosResult> Create(DeliveryD02Case c, DeliveryPosCreateRequest? body = null)
    {
        using var r = await c.Client.Http.PostAsJsonAsync(CreatePath, body ?? await Request(c));
        Assert.True(r.IsSuccessStatusCode, (int)r.StatusCode + " " + await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<DeliveryPosResult>())!;
    }
    internal static async Task Reject(DeliveryD02Case c, DeliveryPosCreateRequest body, HttpStatusCode status, string code)
    {
        using var r = await c.Client.Http.PostAsJsonAsync(CreatePath, body);
        Assert.Equal(status, r.StatusCode);
        Assert.Equal(code, (await r.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("code").GetString());
        await using var db = c.Context(); Assert.Empty(await db.DeliveryOrders.Where(x => x.SourceCartId == c.CartId).ToListAsync());
    }
}
