using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace GaoApp.Infrastructure.Migrations;

public partial class AddReceivingPackagingPhoto : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<byte[]>(
        name: "PackagingPhoto", table: "StockDocumentProvisionalItems", type: "varbinary(max)", nullable: true);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(
        name: "PackagingPhoto", table: "StockDocumentProvisionalItems");
}
