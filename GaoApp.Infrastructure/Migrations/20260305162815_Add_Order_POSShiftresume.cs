using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_Order_POSShiftresume : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Add POSShiftId (nullable trước để không vỡ dữ liệu cũ)
            migrationBuilder.AddColumn<int>(
                name: "POSShiftId",
                table: "Orders",
                type: "int",
                nullable: true);

            // 2) Tạo shift LEGACY cho mỗi StoreId có Orders (nếu chưa có)
            migrationBuilder.Sql(@"
        INSERT INTO POSShifts
            (StoreId, OpenedByUserId, OpenedAtUtc, Status,
             OpeningCash, CashSalesTotal, NonCashSalesTotal, CashInTotal, CashOutTotal, ClosingCashExpected,
             ShiftCode, Note, IsDeleted)
        SELECT DISTINCT
            o.StoreId,
            0,
            SYSUTCDATETIME(),
            2,
            0,0,0,0,0,0,
            CONCAT('LEGACY-', o.StoreId),
            N'Shift hệ thống để gắn đơn cũ trước khi có POSShift',
            0
        FROM Orders o
        WHERE NOT EXISTS (
            SELECT 1 FROM POSShifts s
            WHERE s.StoreId = o.StoreId AND s.ShiftCode = CONCAT('LEGACY-', o.StoreId)
        );
    ");

            // 3) Gán Orders cũ vào shift LEGACY theo Store
            migrationBuilder.Sql(@"
        UPDATE o
        SET o.POSShiftId = s.Id
        FROM Orders o
        JOIN POSShifts s
          ON s.StoreId = o.StoreId
         AND s.ShiftCode = CONCAT('LEGACY-', o.StoreId)
        WHERE o.POSShiftId IS NULL;
    ");

            // 4) Alter POSShiftId => NOT NULL
            migrationBuilder.AlterColumn<int>(
                name: "POSShiftId",
                table: "Orders",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            // 5) Index + FK
            migrationBuilder.CreateIndex(
                name: "IX_Orders_StoreId_POSShiftId",
                table: "Orders",
                columns: new[] { "StoreId", "POSShiftId" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_POSShiftId",
                table: "Orders",
                column: "POSShiftId");

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_POSShifts_POSShiftId",
                table: "Orders",
                column: "POSShiftId",
                principalTable: "POSShifts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
