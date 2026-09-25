using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAcbCallbackStoreRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AcbCallbackRoutes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Host = table.Column<string>(type: "nvarchar(253)", maxLength: 253, nullable: false),
                    TargetStoreId = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcbCallbackRoutes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcbCallbackRoutes_Stores_TargetStoreId",
                        column: x => x.TargetStoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AcbCallbackRouteChanges",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RouteId = table.Column<int>(type: "int", nullable: false),
                    PreviousStoreId = table.Column<int>(type: "int", nullable: true),
                    TargetStoreId = table.Column<int>(type: "int", nullable: true),
                    ActorUserId = table.Column<int>(type: "int", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcbCallbackRouteChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcbCallbackRouteChanges_AcbCallbackRoutes_RouteId",
                        column: x => x.RouteId,
                        principalTable: "AcbCallbackRoutes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcbCallbackRouteChanges_RouteId_ChangedAtUtc",
                table: "AcbCallbackRouteChanges",
                columns: new[] { "RouteId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AcbCallbackRoutes_Host",
                table: "AcbCallbackRoutes",
                column: "Host",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcbCallbackRoutes_TargetStoreId",
                table: "AcbCallbackRoutes",
                column: "TargetStoreId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AcbCallbackRouteChanges");

            migrationBuilder.DropTable(
                name: "AcbCallbackRoutes");
        }
    }
}
