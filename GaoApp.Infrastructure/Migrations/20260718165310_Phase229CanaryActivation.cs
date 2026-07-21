using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase229CanaryActivation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LegalEntityActivationAtUtcSnapshot",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LegalEntityModeCapturedAtUtc",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UseMultiLegalEntity",
                table: "Orders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "LegalEntityActivationEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Action = table.Column<byte>(type: "tinyint", nullable: false),
                    PreviousIsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    NewIsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActivationAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ChangedByUserId = table.Column<int>(type: "int", nullable: true),
                    ChangedByUserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    PreflightPassed = table.Column<bool>(type: "bit", nullable: false),
                    PreflightSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegalEntityActivationEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LegalEntityActivationEvents_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_StoreId_UseMultiLegalEntity_Status_LegalEntityModeCapturedAtUtc_IsDeleted",
                table: "Orders",
                columns: new[] { "StoreId", "UseMultiLegalEntity", "Status", "LegalEntityModeCapturedAtUtc", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_LegalEntityActivationEvents_StoreId_OccurredAtUtc_IsDeleted",
                table: "LegalEntityActivationEvents",
                columns: new[] { "StoreId", "OccurredAtUtc", "IsDeleted" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LegalEntityActivationEvents");

            migrationBuilder.DropIndex(
                name: "IX_Orders_StoreId_UseMultiLegalEntity_Status_LegalEntityModeCapturedAtUtc_IsDeleted",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "LegalEntityActivationAtUtcSnapshot",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "LegalEntityModeCapturedAtUtc",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "UseMultiLegalEntity",
                table: "Orders");
        }
    }
}
