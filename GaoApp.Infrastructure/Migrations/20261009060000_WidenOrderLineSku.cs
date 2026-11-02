using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GaoApp.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261009060000_WidenOrderLineSku")]
public sealed class WidenOrderLineSku : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "Sku",
            table: "OrderLines",
            type: "nvarchar(60)",
            maxLength: 60,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "nvarchar(50)",
            oldMaxLength: 50,
            oldNullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [OrderLines] WHERE DATALENGTH([Sku]) > 100)
                THROW 51030, 'Cannot narrow OrderLines.Sku: existing sale lines contain SKUs longer than 50 UTF-16 units.', 1;
            """);
        migrationBuilder.AlterColumn<string>(
            name: "Sku",
            table: "OrderLines",
            type: "nvarchar(50)",
            maxLength: 50,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "nvarchar(60)",
            oldMaxLength: 60,
            oldNullable: true);
    }
}
