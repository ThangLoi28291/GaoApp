using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261030130000_AddCustomerMarketingCampaignsP6")]
public sealed class AddCustomerMarketingCampaignsP6 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "CustomerMarketingCampaigns",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                CampaignKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PreviewRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RequestFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                MessageText = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                AssetKind = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                AssetFileName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                AssetContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                AssetLength = table.Column<long>(type: "bigint", nullable: true),
                AssetSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                AssetBytes = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                CustomerGroup = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                ConnectionId = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                ConnectionBinding = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                SenderProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SnapshotSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                RecipientCount = table.Column<int>(type: "int", nullable: false),
                ScheduledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                Detail = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<int>(type: "int", nullable: true),
                UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                UpdatedBy = table.Column<int>(type: "int", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                DeletedBy = table.Column<int>(type: "int", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                StoreId = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CustomerMarketingCampaigns", x => x.Id);
                table.CheckConstraint("CK_CustomerMarketingCampaigns_Values", "[StoreId] > 0 AND [RecipientCount] > 0 AND [CampaignKey] <> '00000000-0000-0000-0000-000000000000' AND [PreviewRequestId] <> '00000000-0000-0000-0000-000000000000' AND [SenderProfileId] <> '00000000-0000-0000-0000-000000000000' AND LEN([MessageText]) > 0 AND LEN([RequestFingerprint]) = 64 AND (([AssetBytes] IS NULL AND [AssetKind] IS NULL AND [AssetFileName] IS NULL AND [AssetContentType] IS NULL AND [AssetLength] IS NULL AND [AssetSha256] IS NULL) OR ([AssetBytes] IS NOT NULL AND [AssetKind] IN ('Image','File') AND [AssetFileName] IS NOT NULL AND [AssetContentType] IS NOT NULL AND [AssetLength] > 0 AND [AssetSha256] IS NOT NULL)) AND [Status] IN ('Draft','Scheduled','Dispatching','Completed','Cancelled','NeedsReview')");
                table.ForeignKey("FK_CustomerMarketingCampaigns_Stores_StoreId", x => x.StoreId, "Stores", "Id");
            });

        migrationBuilder.CreateTable(
            name: "CustomerMarketingCampaignRecipients",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                CampaignId = table.Column<int>(type: "int", nullable: false),
                CustomerId = table.Column<int>(type: "int", nullable: false),
                CustomerNameSnapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                BindingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BindingVersion = table.Column<int>(type: "int", nullable: false),
                ConnectionId = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                ConnectionBinding = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                SenderProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ConversationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                NativePeer = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                ZaloJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Attempts = table.Column<int>(type: "int", nullable: false),
                NextAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                LastAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                Detail = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<int>(type: "int", nullable: true),
                UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                UpdatedBy = table.Column<int>(type: "int", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                DeletedBy = table.Column<int>(type: "int", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                StoreId = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CustomerMarketingCampaignRecipients", x => x.Id);
                table.CheckConstraint("CK_CustomerMarketingCampaignRecipients_Values", "[StoreId] > 0 AND [CustomerId] > 0 AND [BindingVersion] > 0 AND [SenderProfileId] <> '00000000-0000-0000-0000-000000000000' AND [Status] IN ('Pending','Dispatching','Accepted','Sent','Skipped','Failed','NeedsReview')");
                table.ForeignKey("FK_CustomerMarketingCampaignRecipients_Campaigns_CampaignId", x => x.CampaignId, "CustomerMarketingCampaigns", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_CustomerMarketingCampaignRecipients_Customers_CustomerId", x => x.CustomerId, "Customers", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_CustomerMarketingCampaignRecipients_InvoiceRecipientBindings_BindingId", x => x.BindingId, "InvoiceRecipientBindings", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_CustomerMarketingCampaignRecipients_Stores_StoreId", x => x.StoreId, "Stores", "Id");
            });

        migrationBuilder.CreateIndex("IX_CustomerMarketingCampaigns_CampaignKey", "CustomerMarketingCampaigns", "CampaignKey", unique: true);
        migrationBuilder.CreateIndex("IX_CustomerMarketingCampaigns_StoreId_PreviewRequestId", "CustomerMarketingCampaigns", new[] { "StoreId", "PreviewRequestId" }, unique: true);
        migrationBuilder.CreateIndex("IX_CustomerMarketingCampaigns_StoreId_Status_ScheduledAtUtc", "CustomerMarketingCampaigns", new[] { "StoreId", "Status", "ScheduledAtUtc" });
        migrationBuilder.CreateIndex("IX_CustomerMarketingCampaignRecipients_BindingId", "CustomerMarketingCampaignRecipients", "BindingId");
        migrationBuilder.CreateIndex("IX_CustomerMarketingCampaignRecipients_CampaignId_CustomerId", "CustomerMarketingCampaignRecipients", new[] { "CampaignId", "CustomerId" }, unique: true);
        migrationBuilder.CreateIndex("IX_CustomerMarketingCampaignRecipients_CustomerId", "CustomerMarketingCampaignRecipients", "CustomerId");
        migrationBuilder.CreateIndex("IX_CustomerMarketingCampaignRecipients_StoreId_Status_NextAttemptAtUtc", "CustomerMarketingCampaignRecipients", new[] { "StoreId", "Status", "NextAttemptAtUtc" });

        migrationBuilder.CreateTable("CustomerMarketingCampaignDispatchGates", columns: table => new
        {
            SenderProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            NextAllowedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_CustomerMarketingCampaignDispatchGates", x => x.SenderProfileId);
            table.CheckConstraint("CK_CustomerMarketingCampaignDispatchGates_Values", "[SenderProfileId] <> '00000000-0000-0000-0000-000000000000'");
        });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM dbo.CustomerMarketingCampaigns) OR EXISTS (SELECT 1 FROM dbo.CustomerMarketingCampaignRecipients) OR EXISTS (SELECT 1 FROM dbo.CustomerMarketingCampaignDispatchGates) THROW 51000, 'Cannot remove marketing campaign history while records exist.', 1;");
        migrationBuilder.DropTable(name: "CustomerMarketingCampaignDispatchGates");
        migrationBuilder.DropTable(name: "CustomerMarketingCampaignRecipients");
        migrationBuilder.DropTable(name: "CustomerMarketingCampaigns");
    }
}
