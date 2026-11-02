using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Services.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb09"), Trait("Category", "StorefrontWeb09")]
public sealed class Web09SalesControlTests(Web01Fixture fixture)
{
    [Fact]
    public async Task Genuine_COD_only_activation_and_emergency_stop_change_only_control_revision_and_actor_audit()
    {
        var c = await Web09Case.CreateAsync(fixture);
        await using var beforeDb = c.Context(); var original = await beforeDb.StorefrontSettings.AsNoTracking().SingleAsync();
        Assert.False(original.IsEnabled); Assert.False(original.QrEnabled); Assert.True(original.CodEnabled);
        var data = Web09Support.SettingsData(original, includeControl: false); var money = await c.Checkout.CountsAsync();
        var audits = await beforeDb.AuditLogs.CountAsync();
        var activated = await c.ActivateAsync(); Assert.True(activated.IsEnabled); Assert.Equal(original.Revision + 1, activated.Revision);
        Assert.Equal(8, Convert.FromBase64String(activated.Version).Length);
        await using (var verify = c.Context())
        {
            Assert.Equal(data, Web09Support.SettingsData(await verify.StorefrontSettings.AsNoTracking().SingleAsync(), false));
            Assert.Equal(audits + 1, await verify.AuditLogs.CountAsync());
            var audit = await verify.AuditLogs.OrderBy(x => x.Id).LastAsync(); Assert.Equal(c.StoreId, audit.StoreId);
            Assert.Equal(c.Seed.UserId, audit.ActorUserId); Assert.True(audit.IsSuccess); Assert.Contains("WEB09", audit.NewValuesJson);
        }
        Assert.Equal(money, await c.Checkout.CountsAsync());
        // The server operator opt-out prevents enable, not the emergency stop.
        var stopped = await c.StopAsync(activationPolicy: false); Assert.False(stopped.IsEnabled); Assert.Equal(activated.Revision + 1, stopped.Revision);
        await using (var verify = c.Context())
        {
            Assert.Equal(data, Web09Support.SettingsData(await verify.StorefrontSettings.AsNoTracking().SingleAsync(), false));
            Assert.Equal(audits + 2, await verify.AuditLogs.CountAsync()); Assert.Equal(c.Seed.UserId, (await verify.AuditLogs.OrderBy(x => x.Id).LastAsync()).ActorUserId);
        }
        Assert.Equal(money, await c.Checkout.CountsAsync());
    }

    [Theory]
    [InlineData("activate")]
    [InlineData("stop")]
    public async Task Current_version_noop_is_not_another_revision_or_audit_but_stale_transition_retry_conflicts(string action)
    {
        var c = await Web09Case.CreateAsync(fixture); if (action == "activate") await c.ActivateAsync();
        var request = await c.CommandAsync(); await using var db = c.Context(); var before = await db.StorefrontSettings.AsNoTracking().SingleAsync();
        var audits = await db.AuditLogs.CountAsync(); var service = c.Sales(db);
        var result = action == "activate" ? await service.ActivateAsync(request) : await service.StopNewOrdersAsync(request);
        Assert.Equal(before.Revision, result.Revision); Assert.Equal(request.Version, result.Version); Assert.Equal(audits, await db.AuditLogs.CountAsync());
        var transition = await c.CommandAsync();
        if (action == "activate") await c.StopAsync(); else await c.ActivateAsync();
        await using var fresh = c.Context();
        await Assert.ThrowsAsync<ConflictAppException>(() => action == "activate" ? c.Sales(fresh).ActivateAsync(transition) : c.Sales(fresh).StopNewOrdersAsync(transition));
    }

    [Fact]
    public async Task Activation_default_server_policy_denies_without_menu_money_or_setting_mutation()
    {
        var c = await Web09Case.CreateAsync(fixture); var request = await c.CommandAsync();
        await using var db = c.Context(); var before = Web09Support.SettingsData(await db.StorefrontSettings.AsNoTracking().SingleAsync());
        var audits = await db.AuditLogs.CountAsync(); var counts = await c.Checkout.CountsAsync();
        var service = c.Sales(db, activation: false); var read = await service.GetAsync(); Assert.False(read.CanActivate); Assert.NotEmpty(read.ActivationBlockers);
        await Assert.ThrowsAsync<ValidationAppException>(() => service.ActivateAsync(request));
        await using var verify = c.Context(); Assert.Equal(before, Web09Support.SettingsData(await verify.StorefrontSettings.AsNoTracking().SingleAsync()));
        Assert.Equal(audits, await verify.AuditLogs.CountAsync()); Assert.Equal(counts, await c.Checkout.CountsAsync());
    }

    [Theory]
    [InlineData("warehouse")]
    [InlineData("legalEntity")]
    [InlineData("noPayments")]
    [InlineData("fee")]
    [InlineData("contact")]
    public async Task Activation_revalidates_current_persisted_prerequisites_and_does_not_mutate_on_blocker(string invalid)
    {
        var c = await Web09Case.CreateAsync(fixture);
        await using (var arrange = c.Context())
        {
            if (invalid == "warehouse") (await arrange.Warehouses.SingleAsync(x => x.Id == c.Seed.WarehouseId)).IsActive = false;
            if (invalid == "legalEntity") (await arrange.LegalEntities.SingleAsync(x => x.Id == c.Seed.LegalEntityId)).IsActive = false;
            var settings = await arrange.StorefrontSettings.SingleAsync();
            if (invalid == "noPayments") settings.CodEnabled = settings.QrEnabled = false;
            if (invalid == "fee")
            {
                Assert.Equal(StorefrontShippingFeeTreatment.StoreServiceFee, settings.ShippingFeeTreatment);
                (await arrange.StorefrontShippingRates.SingleAsync(x => x.IsCurrent)).IsCurrent = false;
            }
            if (invalid == "contact") settings.ContactPhone = "<invalid>";
            await arrange.SaveChangesAsync();
        }
        var request = await c.CommandAsync(); await using var db = c.Context();
        var before = Web09Support.SettingsData(await db.StorefrontSettings.AsNoTracking().SingleAsync()); var counts = await c.Checkout.CountsAsync();
        var dependencies = await DependencyDataAsync(db); var audits = await db.AuditLogs.CountAsync();
        var read = await c.Sales(db).GetAsync(); Assert.False(read.CanActivate); Assert.NotEmpty(read.ActivationBlockers);
        if (invalid == "fee") Assert.Contains("SHIPPING_ZONE_RATE_INVALID", read.ActivationBlockers);
        await Assert.ThrowsAsync<ValidationAppException>(() => c.Sales(db).ActivateAsync(request));
        await using var verify = c.Context(); Assert.Equal(before, Web09Support.SettingsData(await verify.StorefrontSettings.AsNoTracking().SingleAsync()));
        Assert.Equal(dependencies, await DependencyDataAsync(verify)); Assert.Equal(audits, await verify.AuditLogs.CountAsync());
        Assert.Equal(counts, await c.Checkout.CountsAsync());
    }

    [Theory]
    [InlineData("bank")]
    [InlineData("fee")]
    [InlineData("warehouse")]
    [InlineData("contact")]
    public async Task Emergency_stop_is_narrow_even_when_unrelated_current_configuration_is_invalid(string invalid)
    {
        var c = await Web09Case.CreateAsync(fixture); await c.ActivateAsync();
        await using (var arrange = c.Context())
        {
            var settings = await arrange.StorefrontSettings.SingleAsync();
            if (invalid == "bank")
            {
                settings.QrEnabled = true; settings.BankAccountId = c.Seed.BankAccountId; settings.ReceivingLegalEntityId = c.Seed.LegalEntityId;
                settings.QrHoldMinutes = 30; settings.BankTerminalCode = "W09T"; settings.BankUserCode = "W09U";
                (await arrange.StoreBankAccounts.SingleAsync(x => x.Id == c.Seed.BankAccountId)).IsActive = false;
            }
            if (invalid == "fee")
            {
                Assert.Equal(StorefrontShippingFeeTreatment.StoreServiceFee, settings.ShippingFeeTreatment);
                (await arrange.StorefrontShippingRates.SingleAsync(x => x.IsCurrent)).IsCurrent = false;
            }
            if (invalid == "warehouse") (await arrange.Warehouses.SingleAsync(x => x.Id == c.Seed.WarehouseId)).IsActive = false;
            if (invalid == "contact") settings.ContactPhone = "<invalid>";
            await arrange.SaveChangesAsync();
        }
        await using var beforeDb = c.Context(); var original = await beforeDb.StorefrontSettings.AsNoTracking().SingleAsync();
        var untouched = Web09Support.SettingsData(original, false); var counts = await c.Checkout.CountsAsync(); var audits = await beforeDb.AuditLogs.CountAsync();
        var dependencies = await DependencyDataAsync(beforeDb); var read = await c.Sales(beforeDb).GetAsync(); Assert.True(read.CanStop);
        if (invalid == "fee") Assert.Contains("SHIPPING_ZONE_RATE_INVALID", read.ActivationBlockers);
        if (invalid == "bank") Assert.Contains("QR_BINDING_REGISTRATION_INVALID", read.ActivationBlockers);
        var stopped = await c.StopAsync(activationPolicy: false); Assert.False(stopped.IsEnabled); Assert.Equal(original.Revision + 1, stopped.Revision);
        await using var after = c.Context(); Assert.Equal(untouched, Web09Support.SettingsData(await after.StorefrontSettings.AsNoTracking().SingleAsync(), false));
        Assert.Equal(dependencies, await DependencyDataAsync(after));
        Assert.Equal(audits + 1, await after.AuditLogs.CountAsync()); Assert.Equal(counts, await c.Checkout.CountsAsync());
    }

    [Theory]
    [InlineData("", "Reason")]
    [InlineData("not-base64", "Reason")]
    [InlineData("AQID", "Reason")]
    [InlineData("AQIDBAUGBwg= ", "Reason")]
    [InlineData("current", "")]
    [InlineData("current", " ")]
    [InlineData("current", "long")]
    public async Task Malformed_command_is_precise_validation_with_zero_mutation(string version, string reason)
    {
        var c = await Web09Case.CreateAsync(fixture); var command = await c.CommandAsync();
        if (version != "current") command.Version = version; command.Reason = reason == "long" ? new string('x', 1001) : reason;
        await using var db = c.Context(); var before = Web09Support.SettingsData(await db.StorefrontSettings.AsNoTracking().SingleAsync()); var audits = await db.AuditLogs.CountAsync();
        await Assert.ThrowsAsync<ValidationAppException>(() => c.Sales(db).ActivateAsync(command));
        await using var verify = c.Context(); Assert.Equal(before, Web09Support.SettingsData(await verify.StorefrontSettings.AsNoTracking().SingleAsync())); Assert.Equal(audits, await verify.AuditLogs.CountAsync());
    }

    [Theory]
    [InlineData("activate")]
    [InlineData("stop")]
    public async Task Revoked_real_ManageSettings_permission_denies_even_a_current_well_formed_command(string action)
    {
        var c = await Web09Case.CreateAsync(fixture); if (action == "stop") await c.ActivateAsync(); var request = await c.CommandAsync();
        await c.RevokeAsync(StorefrontPermissions.ManageSettings); await using var db = c.Context();
        var before = Web09Support.SettingsData(await db.StorefrontSettings.AsNoTracking().SingleAsync()); var audits = await db.AuditLogs.CountAsync();
        Assert.NotNull(await c.Sales(db).GetAsync());
        await Assert.ThrowsAsync<ForbiddenAppException>(() => action == "activate" ? c.Sales(db).ActivateAsync(request) : c.Sales(db).StopNewOrdersAsync(request));
        await using var verify = c.Context(); Assert.Equal(before, Web09Support.SettingsData(await verify.StorefrontSettings.AsNoTracking().SingleAsync())); Assert.Equal(audits, await verify.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task Concurrent_valid_activations_consume_one_version_and_produce_one_transition_audit()
    {
        var c = await Web09Case.CreateAsync(fixture); var command = await c.CommandAsync();
        await using var beforeDb = c.Context(); var before = await beforeDb.StorefrontSettings.AsNoTracking().SingleAsync(); var audits = await beforeDb.AuditLogs.CountAsync();
        var money = await c.Checkout.CountsAsync(); var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<string> Attempt()
        {
            await go.Task; await using var db = c.Context();
            try { await c.Sales(db).ActivateAsync(command); return "activated"; } catch (ConflictAppException) { return "stale"; }
        }
        var first = Attempt(); var second = Attempt(); go.SetResult(); var results = await Task.WhenAll(first, second);
        Assert.Equal(new[] { "activated", "stale" }, results.Order().ToArray());
        await using var after = c.Context(); var settings = await after.StorefrontSettings.AsNoTracking().SingleAsync();
        Assert.True(settings.IsEnabled); Assert.Equal(before.Revision + 1, settings.Revision); Assert.Equal(audits + 1, await after.AuditLogs.CountAsync()); Assert.Equal(money, await c.Checkout.CountsAsync());
    }
    private static async Task<string> DependencyDataAsync(GaoApp.Infrastructure.Data.AppDbContext db)
    {
        object[] Scalars<T>(IEnumerable<T> rows) where T : class => rows.Select(row => (object)db.Entry(row).CurrentValues.Properties
            .OrderBy(property => property.Name).ToDictionary(property => property.Name, property => db.Entry(row).CurrentValues[property.Name])).ToArray();
        var rates = await db.StorefrontShippingRates.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var banks = await db.StoreBankAccounts.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        return System.Text.Json.JsonSerializer.Serialize(new { Rates = Scalars(rates), Banks = Scalars(banks) });
    }
}
