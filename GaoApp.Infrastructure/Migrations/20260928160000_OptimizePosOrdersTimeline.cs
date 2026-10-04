using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GaoApp.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260928160000_OptimizePosOrdersTimeline")]
public sealed class OptimizePosOrdersTimeline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(name: "ListSortAtUtc", table: "Orders", type: "datetime2", nullable: false,
            computedColumnSql: "COALESCE([CompletedAtUtc], [CreatedAtUtc])", stored: true);
        migrationBuilder.CreateIndex(name: "IX_Orders_ListTimeline", table: "Orders",
            columns: ["StoreId", "ListSortAtUtc", "Id"], descending: [false, true, true], filter: "[IsDeleted] = 0")
            .Annotation("SqlServer:Include", new[] { "Status", "PaymentStatus", "OrderNumber", "GrandTotal", "PaidTotal", "BalanceDue",
                "VoucherDiscountTotal", "CreatedAtUtc", "CompletedAtUtc", "Note" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_Orders_ListTimeline", table: "Orders");
        migrationBuilder.DropColumn(name: "ListSortAtUtc", table: "Orders");
    }
}
