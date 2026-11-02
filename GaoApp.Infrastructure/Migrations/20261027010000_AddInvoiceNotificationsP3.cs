using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GaoApp.Infrastructure.Migrations;

// Additive P3 delta only. No menu, business document or recipient is seeded.
public sealed partial class AddInvoiceNotificationsP3:Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name:"OfficialPdfSha256",table:"InvoiceHeads",type:"nvarchar(64)",maxLength:64,nullable:true);
        migrationBuilder.AddColumn<string>(name:"OfficialZipXmlSha256",table:"InvoiceHeads",type:"nvarchar(64)",maxLength:64,nullable:true);
        migrationBuilder.AddColumn<long>(name:"OfficialPdfByteLength",table:"InvoiceHeads",type:"bigint",nullable:true);
        migrationBuilder.AddColumn<long>(name:"OfficialZipXmlByteLength",table:"InvoiceHeads",type:"bigint",nullable:true);
        migrationBuilder.CreateTable(name:"InvoiceRecipientBindings",columns:table=>new
        {
            Id=table.Column<Guid>(type:"uniqueidentifier",nullable:false),
            StoreId=table.Column<int>(type:"int",nullable:false),
            CustomerId=table.Column<int>(type:"int",nullable:false),
            VerificationRequestId=table.Column<Guid>(type:"uniqueidentifier",nullable:false),
            ConnectionId=table.Column<string>(type:"nvarchar(48)",maxLength:48,nullable:false),
            ConnectionBinding=table.Column<string>(type:"nvarchar(64)",maxLength:64,nullable:false),
            SenderProfileId=table.Column<Guid>(type:"uniqueidentifier",nullable:false),
            ConversationId=table.Column<Guid>(type:"uniqueidentifier",nullable:false),
            IncomingSequence=table.Column<long>(type:"bigint",nullable:false),
            NativePeer=table.Column<string>(type:"nvarchar(128)",maxLength:128,nullable:false),
            Phone=table.Column<string>(type:"nvarchar(20)",maxLength:20,nullable:false),
            Version=table.Column<int>(type:"int",nullable:false),
            VerifiedByUserId=table.Column<int>(type:"int",nullable:false),
            VerifiedAtUtc=table.Column<DateTime>(type:"datetime2",nullable:false),
            VerificationMethod=table.Column<string>(type:"nvarchar(40)",maxLength:40,nullable:false),
            EvidenceReference=table.Column<string>(type:"nvarchar(250)",maxLength:250,nullable:false),
            RevokedAtUtc=table.Column<DateTime>(type:"datetime2",nullable:true),
            RevokedByUserId=table.Column<int>(type:"int",nullable:true),
            RevocationReason=table.Column<string>(type:"nvarchar(250)",maxLength:250,nullable:true),
            RowVersion=table.Column<byte[]>(type:"rowversion",rowVersion:true,nullable:false)
        },constraints:table=>
        {
            table.PrimaryKey("PK_InvoiceRecipientBindings",x=>x.Id);
            table.ForeignKey("FK_InvoiceRecipientBindings_Stores_StoreId",x=>x.StoreId,"Stores","Id",onDelete:ReferentialAction.Restrict);
            table.ForeignKey("FK_InvoiceRecipientBindings_Customers_CustomerId",x=>x.CustomerId,"Customers","Id",onDelete:ReferentialAction.Restrict);
        });
        migrationBuilder.CreateTable(name:"InvoiceNotificationOutbox",columns:table=>new
        {
            Id=table.Column<Guid>(type:"uniqueidentifier",nullable:false),
            StoreId=table.Column<int>(type:"int",nullable:false),
            CustomerId=table.Column<int>(type:"int",nullable:true),
            SourceType=table.Column<string>(type:"nvarchar(16)",maxLength:16,nullable:false),
            SourceId=table.Column<int>(type:"int",nullable:false),
            SourceVersion=table.Column<string>(type:"nvarchar(64)",maxLength:64,nullable:false),
            Kind=table.Column<string>(type:"nvarchar(32)",maxLength:32,nullable:false),
            Code=table.Column<string>(type:"nvarchar(100)",maxLength:100,nullable:false),
            SnapshotJson=table.Column<string>(type:"nvarchar(max)",nullable:false),
            SnapshotSha256=table.Column<string>(type:"nvarchar(64)",maxLength:64,nullable:false),
            RequestId=table.Column<Guid>(type:"uniqueidentifier",nullable:false),
            IdempotencyKey=table.Column<string>(type:"nvarchar(100)",maxLength:100,nullable:false),
            BindingId=table.Column<Guid>(type:"uniqueidentifier",nullable:true),
            BindingVersion=table.Column<int>(type:"int",nullable:true),
            BindingSnapshotJson=table.Column<string>(type:"nvarchar(max)",nullable:true),
            State=table.Column<string>(type:"nvarchar(24)",maxLength:24,nullable:false),
            Detail=table.Column<string>(type:"nvarchar(250)",maxLength:250,nullable:true),
            PreparedJson=table.Column<string>(type:"nvarchar(max)",nullable:true),
            PartsOutcomeJson=table.Column<string>(type:"nvarchar(max)",nullable:true),
            ZaloJobId=table.Column<Guid>(type:"uniqueidentifier",nullable:true),
            LeaseToken=table.Column<Guid>(type:"uniqueidentifier",nullable:true),
            LeaseExpiresAtUtc=table.Column<DateTime>(type:"datetime2",nullable:true),
            FirstAttemptAtUtc=table.Column<DateTime>(type:"datetime2",nullable:true),
            NextAttemptAtUtc=table.Column<DateTime>(type:"datetime2",nullable:false),
            Attempts=table.Column<int>(type:"int",nullable:false),
            CreatedAtUtc=table.Column<DateTime>(type:"datetime2",nullable:false),
            UpdatedAtUtc=table.Column<DateTime>(type:"datetime2",nullable:true),
            CreatedByUserId=table.Column<int>(type:"int",nullable:true),
            RowVersion=table.Column<byte[]>(type:"rowversion",rowVersion:true,nullable:false)
        },constraints:table=>
        {
            table.PrimaryKey("PK_InvoiceNotificationOutbox",x=>x.Id);
            table.ForeignKey("FK_InvoiceNotificationOutbox_Stores_StoreId",x=>x.StoreId,"Stores","Id",onDelete:ReferentialAction.Restrict);
            table.ForeignKey("FK_InvoiceNotificationOutbox_InvoiceRecipientBindings_BindingId",x=>x.BindingId,"InvoiceRecipientBindings","Id",onDelete:ReferentialAction.Restrict);
        });
        migrationBuilder.CreateTable(name:"InvoiceNotificationAudit",columns:table=>new
        {
            Id=table.Column<Guid>(type:"uniqueidentifier",nullable:false),
            StoreId=table.Column<int>(type:"int",nullable:false),
            BindingId=table.Column<Guid>(type:"uniqueidentifier",nullable:true),
            OutboxId=table.Column<Guid>(type:"uniqueidentifier",nullable:true),
            ActorUserId=table.Column<int>(type:"int",nullable:true),
            Action=table.Column<string>(type:"nvarchar(50)",maxLength:50,nullable:false),
            Detail=table.Column<string>(type:"nvarchar(250)",maxLength:250,nullable:false),
            CreatedAtUtc=table.Column<DateTime>(type:"datetime2",nullable:false)
        },constraints:table=>
        {
            table.PrimaryKey("PK_InvoiceNotificationAudit",x=>x.Id);
        });
        migrationBuilder.CreateIndex("IX_InvoiceRecipientBindings_StoreId_CustomerId","InvoiceRecipientBindings",new[]{"StoreId","CustomerId"},unique:true,filter:"[RevokedAtUtc] IS NULL");
        migrationBuilder.CreateIndex("IX_InvoiceRecipientBindings_StoreId_ConnectionId_ConversationId","InvoiceRecipientBindings",new[]{"StoreId","ConnectionId","ConversationId"},unique:true,filter:"[RevokedAtUtc] IS NULL");
        migrationBuilder.CreateIndex("IX_InvoiceRecipientBindings_CustomerId","InvoiceRecipientBindings","CustomerId");
        migrationBuilder.CreateIndex("IX_InvoiceRecipientBindings_StoreId_VerificationRequestId","InvoiceRecipientBindings",new[]{"StoreId","VerificationRequestId"},unique:true);
        migrationBuilder.CreateIndex("IX_InvoiceNotificationOutbox_StoreId_SourceType_SourceId_SourceVersion_Kind","InvoiceNotificationOutbox",new[]{"StoreId","SourceType","SourceId","SourceVersion","Kind"},unique:true);
        migrationBuilder.CreateIndex("IX_InvoiceNotificationOutbox_StoreId_RequestId","InvoiceNotificationOutbox",new[]{"StoreId","RequestId"},unique:true);
        migrationBuilder.CreateIndex("IX_InvoiceNotificationOutbox_State_NextAttemptAtUtc","InvoiceNotificationOutbox",new[]{"State","NextAttemptAtUtc"});
        migrationBuilder.CreateIndex("IX_InvoiceNotificationOutbox_BindingId","InvoiceNotificationOutbox","BindingId");
        migrationBuilder.CreateIndex("IX_InvoiceNotificationAudit_StoreId_CreatedAtUtc","InvoiceNotificationAudit",new[]{"StoreId","CreatedAtUtc"});
        migrationBuilder.AddCheckConstraint("CK_InvoiceRecipientBindings_Proof","InvoiceRecipientBindings","[StoreId]>0 AND [CustomerId]>0 AND [Version]>0 AND [IncomingSequence]>0 AND [VerifiedByUserId]>0");
        migrationBuilder.AddCheckConstraint("CK_InvoiceNotificationOutbox_Source","InvoiceNotificationOutbox","[StoreId]>0 AND [SourceId]>0 AND [SourceType] IN ('Order','Invoice') AND [Kind] IN ('Receipt','OfficialPdf','OfficialZipXml','OfficialXml','OfficialLink','CorrectionPdf','CorrectionZipXml','CorrectionXml','CorrectionLink','DraftPreview')");
        migrationBuilder.AddCheckConstraint("CK_InvoiceNotificationOutbox_State","InvoiceNotificationOutbox","[State] IN ('Pending','Dispatching','Accepted','Sent','PartiallySent','NeedsReview','Failed','Blocked','Cancelled')");
        migrationBuilder.AddCheckConstraint("CK_InvoiceNotificationOutbox_Json","InvoiceNotificationOutbox","ISJSON([SnapshotJson])=1 AND ([BindingSnapshotJson] IS NULL OR ISJSON([BindingSnapshotJson])=1) AND ([PreparedJson] IS NULL OR ISJSON([PreparedJson])=1)");
        migrationBuilder.AddCheckConstraint("CK_InvoiceHeads_NotificationArtifactProof","InvoiceHeads","([OfficialPdfSha256] IS NULL AND [OfficialPdfByteLength] IS NULL OR LEN([OfficialPdfSha256])=64 AND [OfficialPdfByteLength]>0) AND ([OfficialZipXmlSha256] IS NULL AND [OfficialZipXmlByteLength] IS NULL OR LEN([OfficialZipXmlSha256])=64 AND [OfficialZipXmlByteLength]>0)");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS(SELECT 1 FROM [InvoiceNotificationOutbox]) OR EXISTS(SELECT 1 FROM [InvoiceRecipientBindings]) OR EXISTS(SELECT 1 FROM [InvoiceNotificationAudit]) OR EXISTS(SELECT 1 FROM [InvoiceHeads] WHERE [OfficialPdfSha256] IS NOT NULL OR [OfficialZipXmlSha256] IS NOT NULL)
                THROW 51000,'Cannot remove P3 while invoice notification or recipient evidence exists.',1;
            """);
        migrationBuilder.DropTable("InvoiceNotificationOutbox");
        migrationBuilder.DropTable("InvoiceRecipientBindings");
        migrationBuilder.DropTable("InvoiceNotificationAudit");
        migrationBuilder.DropCheckConstraint("CK_InvoiceHeads_NotificationArtifactProof","InvoiceHeads");
        migrationBuilder.DropColumn("OfficialPdfSha256","InvoiceHeads");
        migrationBuilder.DropColumn("OfficialPdfByteLength","InvoiceHeads");
        migrationBuilder.DropColumn("OfficialZipXmlSha256","InvoiceHeads");
        migrationBuilder.DropColumn("OfficialZipXmlByteLength","InvoiceHeads");
    }
}
