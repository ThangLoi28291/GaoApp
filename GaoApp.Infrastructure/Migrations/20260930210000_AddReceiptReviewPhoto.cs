using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace GaoApp.Infrastructure.Migrations;
public partial class AddReceiptReviewPhoto : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<byte[]>("ReviewPhoto", "StockDocumentProvisionalItems", type: "varbinary(max)", nullable: true);
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn("ReviewPhoto", "StockDocumentProvisionalItems");
}
