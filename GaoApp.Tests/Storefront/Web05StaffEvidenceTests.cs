using System.Text.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using GaoApp.Infrastructure.Services.Storefront;
using GaoApp.Web.Areas.Admin.Controllers;
using Microsoft.AspNetCore.Authorization;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb05"), Trait("Category", "StorefrontWeb05")]
public sealed class Web05StaffEvidenceTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData(false, true)][InlineData(true, false)]
    public async Task Staff_bank_evidence_service_requires_authenticated_same_store_effective_permission(bool authenticated, bool allowed)
    {
        var p = await Web05Prepared.CreateAsync(fixture); await using var db = p.Case.Context();
        var service = new StorefrontBankEvidenceReadService(db, new Web01Actor(p.Case.Seed.UserId, authenticated), new Web01Permissions(p.Case.StoreId, p.Case.Seed.UserId, allowed ? [StorefrontPermissions.ViewOrders] : []));
        await Assert.ThrowsAsync<ForbiddenAppException>(() => service.GetAsync(p.OrderId));
    }
    [Theory]
    [InlineData("45000")][InlineData("1")]
    public async Task Authorized_staff_reads_full_exact_bank_amount_precision_and_review_without_secret_payload_or_guest_capability(string amountText)
    {
        var p = await Web05Prepared.CreateAsync(fixture); var amount = decimal.Parse(amountText, System.Globalization.CultureInfo.InvariantCulture); p.Bank.Pay(p.Reference, amount); await p.Case.ProcessAsync(p.IntentId, p.Bank);
        await using var db = p.Case.Context(); var service = new StorefrontBankEvidenceReadService(db, new Web01Actor(p.Case.Seed.UserId), new Web01Permissions(p.Case.StoreId, p.Case.Seed.UserId, [StorefrontPermissions.ViewOrders]));
        var read = await service.GetAsync(p.OrderId); using var json = JsonDocument.Parse(JsonSerializer.Serialize(read, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var receipt = Assert.Single(json.RootElement.GetProperty("receipts").EnumerateArray()); Assert.Equal(amount, receipt.GetProperty("amount").GetDecimal());
        var text = json.RootElement.GetRawText(); foreach (var secret in new[] { Web04Support.GuestToken(p.Placed), "protectedEvidence", "clientSecret", "callbackApiKey", "bindingSnapshot", "accountNumber", "GuestTokenHash" }) Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(typeof(StorefrontOrdersController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>(), x => x.Policy == StorefrontPermissions.ViewOrders);
    }
    [Fact]
    public async Task Authorized_foreign_store_cannot_enumerate_another_store_bank_evidence_by_numeric_order_id()
    {
        var p = await Web05Prepared.CreateAsync(fixture); var foreign = await fixture.Web05Async(); await using var db = foreign.Context();
        var service = new StorefrontBankEvidenceReadService(db, new Web01Actor(foreign.Seed.UserId), new Web01Permissions(foreign.StoreId, foreign.Seed.UserId, [StorefrontPermissions.ViewOrders]));
        await Assert.ThrowsAsync<NotFoundAppException>(() => service.GetAsync(p.OrderId));
    }
}
