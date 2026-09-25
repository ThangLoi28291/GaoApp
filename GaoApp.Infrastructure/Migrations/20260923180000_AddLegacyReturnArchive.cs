using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GaoApp.Infrastructure.Migrations;

// Intentionally outside the operational SalesReturn model: this archive cannot post stock/refunds.
[DbContext(typeof(AppDbContext))]
[Migration("20260923180000_AddLegacyReturnArchive")]
public sealed class AddLegacyReturnArchive : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "LegacyReturnArchives",
            schema: "dbo",
            columns: table => new
            {
                Id = table.Column<long>(
                        type: "bigint",
                        nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),

                StoreId = table.Column<int>(
                    type: "int",
                    nullable: false),

                LegacyOrderId = table.Column<long>(
                    type: "bigint",
                    nullable: false),

                OccurredAtUtc = table.Column<DateTime>(
                     type: "datetime2",
                    nullable: true),

                LegacyCustomerId = table.Column<long>(
                    type: "bigint",
                    nullable: true),

                CustomerName = table.Column<string>(
                    type: "nvarchar(400)",
                    maxLength: 400,
                    nullable: true),

                LegacyUserId = table.Column<long>(
                    type: "bigint",
                    nullable: true),

                EmployeeName = table.Column<string>(
                    type: "nvarchar(400)",
                    maxLength: 400,
                    nullable: true),

                SourceTotal = table.Column<decimal>(
                    type: "decimal(18,2)",
                    nullable: true),

                SourcePaymentFlag = table.Column<bool>(
                    type: "bit",
                    nullable: true),

                HeaderJson = table.Column<string>(
                    type: "nvarchar(max)",
                    nullable: false),

                DetailsJson = table.Column<string>(
                    type: "nvarchar(max)",
                    nullable: false),

                ImportedAtUtc = table.Column<DateTime>(
                    type: "datetime2",
                    nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_LegacyReturnArchives",
                    x => x.Id);

                table.UniqueConstraint(
                    "UQ_LegacyReturnArchives_Source",
                    x => new
                    {
                        x.StoreId,
                        x.LegacyOrderId
                    });

                table.CheckConstraint(
                    "CK_LegacyReturnArchives_Json",
                    "ISJSON([HeaderJson])=1 AND ISJSON([DetailsJson])=1");

                table.ForeignKey(
                    name: "FK_LegacyReturnArchives_Stores",
                    column: x => x.StoreId,
                    principalSchema: "dbo",
                    principalTable: "Stores",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.NoAction);
            });

        migrationBuilder.CreateIndex(
            name: "IX_LegacyReturnArchives_Date",
            schema: "dbo",
            table: "LegacyReturnArchives",
            columns: new[]
            {
            "StoreId",
            "OccurredAtUtc",
            "LegacyOrderId"
            },
            descending: new[]
            {
            false,
            true,
            true
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
        IF EXISTS(SELECT 1 FROM dbo.LegacyReturnArchives)
            THROW 55300,'Cannot remove a non-empty historical return archive. Export/restore must be planned explicitly.',1;
        """);

        migrationBuilder.DropTable(
            name: "LegacyReturnArchives",
            schema: "dbo");
    }
}
