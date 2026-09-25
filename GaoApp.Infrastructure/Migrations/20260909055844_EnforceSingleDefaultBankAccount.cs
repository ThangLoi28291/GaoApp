using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations;

public partial class EnforceSingleDefaultBankAccount : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Never choose a payment destination implicitly when existing defaults conflict.
        migrationBuilder.Sql("""
            IF EXISTS (SELECT StoreId FROM StoreBankAccounts
                       WHERE IsDefault = 1 AND IsDeleted = 0
                       GROUP BY StoreId HAVING COUNT(*) > 1)
                THROW 51001, 'Multiple default bank accounts exist in a store. Select one default per store before retrying migration.', 1;
            """);
        migrationBuilder.CreateIndex(
            name: "UX_StoreBankAccounts_OneDefaultPerStore",
            table: "StoreBankAccounts", column: "StoreId", unique: true,
            filter: "[IsDefault] = 1 AND [IsDeleted] = 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropIndex(name: "UX_StoreBankAccounts_OneDefaultPerStore", table: "StoreBankAccounts");
}
