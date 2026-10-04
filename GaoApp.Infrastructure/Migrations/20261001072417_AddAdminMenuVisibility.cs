using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminMenuVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdminMenuVisibilitySettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<int>(type: "int", nullable: true),
                    UserInStoreId = table.Column<int>(type: "int", nullable: true),
                    HiddenMenuIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
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
                    table.PrimaryKey("PK_AdminMenuVisibilitySettings", x => x.Id);
                    table.CheckConstraint("CK_AdminMenuVisibilitySettings_Target", "([RoleId] IS NOT NULL AND [UserInStoreId] IS NULL) OR ([RoleId] IS NULL AND [UserInStoreId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_AdminMenuVisibilitySettings_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AdminMenuVisibilitySettings_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AdminMenuVisibilitySettings_UserInStores_UserInStoreId",
                        column: x => x.UserInStoreId,
                        principalTable: "UserInStores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminMenuVisibilitySettings_RoleId",
                table: "AdminMenuVisibilitySettings",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminMenuVisibilitySettings_StoreId_RoleId",
                table: "AdminMenuVisibilitySettings",
                columns: new[] { "StoreId", "RoleId" },
                unique: true,
                filter: "[RoleId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AdminMenuVisibilitySettings_StoreId_UserInStoreId",
                table: "AdminMenuVisibilitySettings",
                columns: new[] { "StoreId", "UserInStoreId" },
                unique: true,
                filter: "[UserInStoreId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AdminMenuVisibilitySettings_UserInStoreId",
                table: "AdminMenuVisibilitySettings",
                column: "UserInStoreId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminMenuVisibilitySettings");
        }
    }
}
