using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GaoApp.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260930160000_AllowSignedPOSExpectedCash")]
public sealed class AllowSignedPOSExpectedCash : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropCheckConstraint("CK_POSShifts_ClosingCashExpected_NonNegative", "POSShifts");

    // Rolling back requires resolving any negative expected balances first; never rewrite their values.
    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.AddCheckConstraint("CK_POSShifts_ClosingCashExpected_NonNegative", "POSShifts", "[ClosingCashExpected] >= 0");
}
