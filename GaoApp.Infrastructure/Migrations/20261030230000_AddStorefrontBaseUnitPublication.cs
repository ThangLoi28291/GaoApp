using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations;

public partial class AddStorefrontBaseUnitPublication : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<bool>(
        name: "BaseUnitIsPublished", table: "StorefrontVariants", type: "bit", nullable: false, defaultValue: false);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [StorefrontVariants] WHERE [BaseUnitIsPublished] = 1) THROW 51093, 'Cannot remove storefront base-unit publication while a base unit is selected.', 1;");
        migrationBuilder.DropColumn(name: "BaseUnitIsPublished", table: "StorefrontVariants");
    }
}
