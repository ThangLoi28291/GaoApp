using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace GaoApp.Infrastructure.Migrations;
public partial class AddReceiptIntakeReviewDraft : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("ReviewDraftJson", "StockDocumentProvisionalItems", type: "nvarchar(max)", maxLength: 8000, nullable: true);
        migrationBuilder.AddColumn<string>("OriginalDeclarationJson", "StockDocumentProvisionalItems", type: "nvarchar(max)", maxLength: 8000, nullable: true);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("ReviewDraftJson", "StockDocumentProvisionalItems");
        migrationBuilder.DropColumn("OriginalDeclarationJson", "StockDocumentProvisionalItems");
    }
}
