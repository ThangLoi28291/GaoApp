using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace GaoApp.Infrastructure.Migrations;

public partial class AddPosCollectionIdempotency : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(name: "ClientRequestId", table: "OrderPayments", type: "uniqueidentifier", nullable: true);
        // Legacy/ACB entries stay null; cancelled collections keep reserving their key.
        migrationBuilder.CreateIndex(name: "UX_OrderPayments_StoreId_ClientRequestId", table: "OrderPayments",
            columns: new[] { "StoreId", "ClientRequestId" }, unique: true, filter: "[ClientRequestId] IS NOT NULL");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "UX_OrderPayments_StoreId_ClientRequestId", table: "OrderPayments");
        migrationBuilder.DropColumn(name: "ClientRequestId", table: "OrderPayments");
    }
}