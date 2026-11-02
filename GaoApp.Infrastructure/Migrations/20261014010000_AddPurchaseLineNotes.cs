using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GaoApp.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261014010000_AddPurchaseLineNotes")]
public sealed class AddPurchaseLineNotes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("Note", "PurchaseRequestLines", "nvarchar(1000)",
            maxLength: 1000, nullable: true);
        migrationBuilder.AddColumn<string>("Note", "PurchaseOrderLines", "nvarchar(1000)",
            maxLength: 1000, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("Note", "PurchaseOrderLines");
        migrationBuilder.DropColumn("Note", "PurchaseRequestLines");
    }
}
