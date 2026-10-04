using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GaoApp.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260929090000_OptimizePosOrdersCount")]
public sealed class OptimizePosOrdersCount : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.CreateIndex(name: "IX_Orders_ListCount", table: "Orders",
                columns: ["StoreId", "Status"], filter: "[IsDeleted] = 0")
            .Annotation("SqlServer:Include", new[] { "CreatedAtUtc", "CompletedAtUtc" });

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropIndex(name: "IX_Orders_ListCount", table: "Orders");
}
