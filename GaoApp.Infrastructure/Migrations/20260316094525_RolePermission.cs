using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RolePermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserStores_Stores_StoreId",
                table: "UserStores");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserStores",
                table: "UserStores");

            migrationBuilder.RenameTable(
                name: "UserStores",
                newName: "UserInStores");

            migrationBuilder.RenameIndex(
                name: "IX_UserStores_StoreId_UserId",
                table: "UserInStores",
                newName: "IX_UserInStores_StoreId_UserId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserInStores",
                table: "UserInStores",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "Permissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    GroupName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    IsSystemRole = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_Roles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Roles_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    PermissionId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RolePermissions_Permissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "Permissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RolePermissions_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserInStores_RoleId",
                table: "UserInStores",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_UserInStores_StoreId_RoleId",
                table: "UserInStores",
                columns: new[] { "StoreId", "RoleId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserInStores_UserId_StoreId_IsActive",
                table: "UserInStores",
                columns: new[] { "UserId", "StoreId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Permissions_Code",
                table: "Permissions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_PermissionId",
                table: "RolePermissions",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_RoleId_PermissionId",
                table: "RolePermissions",
                columns: new[] { "RoleId", "PermissionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Roles_StoreId_Code",
                table: "Roles",
                columns: new[] { "StoreId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Roles_StoreId_Name",
                table: "Roles",
                columns: new[] { "StoreId", "Name" });

            migrationBuilder.AddForeignKey(
                name: "FK_UserInStores_Roles_RoleId",
                table: "UserInStores",
                column: "RoleId",
                principalTable: "Roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserInStores_Stores_StoreId",
                table: "UserInStores",
                column: "StoreId",
                principalTable: "Stores",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserInStores_Roles_RoleId",
                table: "UserInStores");

            migrationBuilder.DropForeignKey(
                name: "FK_UserInStores_Stores_StoreId",
                table: "UserInStores");

            migrationBuilder.DropTable(
                name: "RolePermissions");

            migrationBuilder.DropTable(
                name: "Permissions");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserInStores",
                table: "UserInStores");

            migrationBuilder.DropIndex(
                name: "IX_UserInStores_RoleId",
                table: "UserInStores");

            migrationBuilder.DropIndex(
                name: "IX_UserInStores_StoreId_RoleId",
                table: "UserInStores");

            migrationBuilder.DropIndex(
                name: "IX_UserInStores_UserId_StoreId_IsActive",
                table: "UserInStores");

            migrationBuilder.RenameTable(
                name: "UserInStores",
                newName: "UserStores");

            migrationBuilder.RenameIndex(
                name: "IX_UserInStores_StoreId_UserId",
                table: "UserStores",
                newName: "IX_UserStores_StoreId_UserId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserStores",
                table: "UserStores",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_UserStores_Stores_StoreId",
                table: "UserStores",
                column: "StoreId",
                principalTable: "Stores",
                principalColumn: "Id");
        }
    }
}
