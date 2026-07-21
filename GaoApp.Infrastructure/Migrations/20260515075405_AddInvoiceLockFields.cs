using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceLockFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsLocked",
                table: "InvoiceHeads",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LockReason",
                table: "InvoiceHeads",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockedAtUtc",
                table: "InvoiceHeads",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LockedByUserId",
                table: "InvoiceHeads",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHeads_StoreId_IsLocked",
                table: "InvoiceHeads",
                columns: new[] { "StoreId", "IsLocked" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InvoiceHeads_StoreId_IsLocked",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "IsLocked",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "LockReason",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "LockedAtUtc",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "LockedByUserId",
                table: "InvoiceHeads");
        }
    }
}
