using System.Net;
using System.Text.Json;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

public sealed class ViettelTaxIdentityRepairSqlServerTests
{
    [Fact]
    public async Task Restore_api_tax_preserves_seller_tax_and_repairs_only_safe_local_drafts()
    {
        const string login = "8341099579-001", issuer = "070091008655";
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var account = await app.AddAccountAsync(store, "*");
        var otherAccount = await app.AddAccountAsync(app.Stores[1], "*");
        var draftOrder = await InvoiceOrderDraftSqlServerTests.SeedOrder(app, account, "POS-TAX-LOCAL", InvoiceIssuanceRoute.Manual);
        var issuedOrder = await InvoiceOrderDraftSqlServerTests.SeedOrder(app, account, "POS-TAX-ISSUED", InvoiceIssuanceRoute.Manual);
        var recoveryOrder = await InvoiceOrderDraftSqlServerTests.SeedOrder(app, account, "POS-TAX-RECOVERY", InvoiceIssuanceRoute.Manual);
        var otherOrder = await InvoiceOrderDraftSqlServerTests.SeedOrder(app, otherAccount, "POS-TAX-OTHER", InvoiceIssuanceRoute.Manual);
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var setting = await db.InvoiceProviderSettings.SingleAsync();
        setting.Username = login; setting.SupplierTaxCode = issuer;
        await db.SaveChangesAsync();
        using var client = await app.LoginAsync(account);
        var local = await Create(client, draftOrder);
        var localId = local.GetProperty("invoices")[0].GetProperty("id").GetInt32();
        var issued = await Create(client, issuedOrder);
        var issuedId = issued.GetProperty("invoices")[0].GetProperty("id").GetInt32();
        var issuedHead = await db.InvoiceHeads.SingleAsync(x => x.Id == issuedId);
        issuedHead.SupplierTaxCode = login;
        issuedHead.ProviderStatus = InvoiceProviderStatus.Issued; issuedHead.IsLocked = true;
        issuedHead.ProviderInvoiceNo = "TAX-HISTORY-001"; issuedHead.IssuedAtUtc = DateTime.UtcNow;
        var inactive = new InvoiceProviderSetting { StoreId = store.StoreId, ProviderCode = "VIETTEL", Username = "inactive-test", Password = "test",
            SupplierTaxCode = "0100109106-507", TemplateCode = "2/LKD3", InvoiceSeries = "C26MTM", InvoiceType = "2", IsActive = false };
        db.InvoiceProviderSettings.Add(inactive); await db.SaveChangesAsync();
        var entity = await db.LegalEntities.SingleAsync();
        entity.TaxCode = issuer; entity.InvoiceProviderSettingId = inactive.Id;
        var line = await db.OrderLines.SingleAsync(x => x.OrderId == recoveryOrder);
        db.OrderLegalEntityAllocations.Add(new()
        {
            StoreId = store.StoreId, OrderId = recoveryOrder, OrderLineId = line.Id,
            ProductVariantId = store.VariantId, LegalEntityId = entity.Id, WarehouseId = store.WarehouseId,
            SalePriority = 1, Quantity = 2, BaseQuantity = 2, UnitPrice = 5000, LineTotal = 10000, NetAmount = 10000
        });
        var order = await db.Orders.SingleAsync(x => x.Id == recoveryOrder); order.LegalEntityCount = 1;
        await db.SaveChangesAsync();
        using (var failure = await client.Http.PostAsync("/Admin/Invoice/CreateOrderDraft", new FormUrlEncodedContent(new Dictionary<string,string>
            { ["orderId"] = recoveryOrder.ToString() }))) Assert.Equal(HttpStatusCode.BadRequest, failure.StatusCode);
        await using var otherDb = app.Database.CreateTenantContext(otherAccount.Store.StoreId);
        var otherSetting = await otherDb.InvoiceProviderSettings.SingleAsync();
        otherSetting.Username = login; otherSetting.SupplierTaxCode = login; await otherDb.SaveChangesAsync();
        using var otherClient = await app.LoginAsync(otherAccount);
        var foreignHead = await Create(otherClient, otherOrder);
        var foreignId = foreignHead.GetProperty("invoices")[0].GetProperty("id").GetInt32();

        var sql = await File.ReadAllTextAsync(Path.Combine(FullApplicationFixture.SourceRoot(), "scripts/repair-viettel-hkd-tax-identity.sql"));
        sql = sql.Replace("$(StoreId)", store.StoreId.ToString()).Replace("$(LegalEntityId)", entity.Id.ToString()).Replace("$(SettingId)", setting.Id.ToString());
        var encryptedPassword = (await db.InvoiceProviderSettings.AsNoTracking().SingleAsync(x => x.Id == setting.Id)).Password;
        await db.Database.ExecuteSqlRawAsync(sql.Replace("$(ApplyRepair)", "0"));
        Assert.Equal(issuer, (await db.InvoiceProviderSettings.AsNoTracking().SingleAsync(x => x.Id == setting.Id)).SupplierTaxCode);
        Assert.Equal(inactive.Id, (await db.LegalEntities.AsNoTracking().SingleAsync()).InvoiceProviderSettingId);
        await db.Database.ExecuteSqlRawAsync(sql.Replace("$(ApplyRepair)", "1"));
        var fixedSetting = await db.InvoiceProviderSettings.AsNoTracking().SingleAsync(x => x.Id == setting.Id);
        Assert.Equal(login, fixedSetting.Username); Assert.Equal(login, fixedSetting.SupplierTaxCode); Assert.Equal(encryptedPassword, fixedSetting.Password);
        var fixedEntity = await db.LegalEntities.AsNoTracking().SingleAsync();
        Assert.Equal(setting.Id, fixedEntity.InvoiceProviderSettingId); Assert.Equal(issuer, fixedEntity.TaxCode);
        var fixedDraft = await db.InvoiceHeads.AsNoTracking().SingleAsync(x => x.Id == localId);
        Assert.Equal(login, fixedDraft.SupplierTaxCode); Assert.Equal(InvoiceProviderStatus.LocalDraft, fixedDraft.ProviderStatus);
        var history = await db.InvoiceHeads.AsNoTracking().SingleAsync(x => x.Id == issuedId);
        Assert.Equal(login, history.SupplierTaxCode); Assert.Equal("TAX-HISTORY-001", history.ProviderInvoiceNo); Assert.True(history.IsLocked);
        Assert.Equal(login, (await otherDb.InvoiceProviderSettings.AsNoTracking().SingleAsync()).SupplierTaxCode);
        Assert.Equal(login, (await otherDb.InvoiceHeads.AsNoTracking().SingleAsync(x => x.Id == foreignId)).SupplierTaxCode);

        var created = await Create(client, recoveryOrder);
        Assert.True(created.GetProperty("created").GetBoolean());
        var newId = created.GetProperty("invoices")[0].GetProperty("id").GetInt32();
        var newHead = await db.InvoiceHeads.AsNoTracking().Include(x => x.Details).SingleAsync(x => x.Id == newId);
        Assert.Equal(login, newHead.SupplierTaxCode); Assert.Equal(entity.Id, newHead.LegalEntityId);
        Assert.Equal(setting.Id, newHead.InvoiceProviderSettingId); Assert.Empty(newHead.Details);
        await db.Database.ExecuteSqlRawAsync(sql.Replace("$(ApplyRepair)", "1"));
        Assert.Equal(fixedDraft.UpdatedAtUtc, (await db.InvoiceHeads.AsNoTracking().SingleAsync(x => x.Id == localId)).UpdatedAtUtc);
        Assert.Equal(3, await db.InvoiceHeads.CountAsync());
    }

    private static async Task<JsonElement> Create(FullApplicationFixture.Client client, int orderId)
    {
        using var response = await client.Http.PostAsync("/Admin/Invoice/CreateOrderDraft", new FormUrlEncodedContent(new Dictionary<string,string>
            { ["orderId"] = orderId.ToString() }));
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }
}
