using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Configuration;

public sealed class CustomerMarketingCampaignModelTests
{
    [Fact]
    public async Task Campaign_entities_are_in_current_snapshot()
    {
        await using var fixture = new InventoryPostingLocalDb();
        await using var db = fixture.CreateHostContext();
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.NotNull(db.Model.FindEntityType(typeof(GaoApp.Domain.Entities.CustomerMarketingCampaign)));
        Assert.NotNull(db.Model.FindEntityType(typeof(GaoApp.Domain.Entities.CustomerMarketingCampaignRecipient)));
        var campaign = db.Model.FindEntityType(typeof(GaoApp.Domain.Entities.CustomerMarketingCampaign))!;
        Assert.Equal("varbinary(max)", campaign.FindProperty(nameof(GaoApp.Domain.Entities.CustomerMarketingCampaign.AssetBytes))!.GetColumnType());
    }

    [Fact]
    public void Campaign_worker_is_off_by_default_and_cannot_expand_the_pilot_phone()
    {
        var options = new GaoApp.Web.Services.CustomerService.CustomerMarketingCampaignOptions();
        Assert.False(options.Enabled);
        Assert.Equal("0398946174", options.PilotPhone);
        Assert.True(options.Valid());
        options.PilotPhone = "0900000000";
        Assert.False(options.Valid());
    }

    [Fact]
    public void Preview_fingerprint_is_stable_and_covers_asset_and_audience_snapshot()
    {
        var at = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
        var text = GaoApp.Web.Services.CustomerService.CustomerMarketingCampaignFingerprint.Compute("name", "body", null, "c", "b", Guid.Parse("11111111-1111-1111-1111-111111111111"), at, "a", null, null);
        Assert.Equal(text, GaoApp.Web.Services.CustomerService.CustomerMarketingCampaignFingerprint.Compute("name", "body", null, "c", "b", Guid.Parse("11111111-1111-1111-1111-111111111111"), at, "a", null, null));
        Assert.NotEqual(text, GaoApp.Web.Services.CustomerService.CustomerMarketingCampaignFingerprint.Compute("name", "body", null, "c", "b", Guid.Parse("11111111-1111-1111-1111-111111111111"), at, "a", null, new string('b', 64)));
        Assert.NotEqual(text, GaoApp.Web.Services.CustomerService.CustomerMarketingCampaignFingerprint.Compute("name", "body", null, "c", "b", Guid.Parse("11111111-1111-1111-1111-111111111111"), at, "c", null, null));
        var asset = new GaoApp.Web.Services.CustomerService.CustomerOutgoingAttachment("File", "invoice.pdf", "application/pdf", 1024);
        var fileText = GaoApp.Web.Services.CustomerService.CustomerMarketingCampaignFingerprint.Compute("name", "body", null, "c", "b", Guid.Parse("11111111-1111-1111-1111-111111111111"), at, "a", asset, new string('d', 64));
        Assert.NotEqual(fileText, GaoApp.Web.Services.CustomerService.CustomerMarketingCampaignFingerprint.Compute("name", "body", null, "c", "b", Guid.Parse("11111111-1111-1111-1111-111111111111"), at, "a", asset with { FileName = "bill.pdf" }, new string('d', 64)));
    }
}
