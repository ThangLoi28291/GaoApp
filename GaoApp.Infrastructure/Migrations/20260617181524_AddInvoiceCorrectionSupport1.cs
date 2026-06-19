using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceCorrectionSupport1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceCorrectionCases_InvoiceHeads_NewInvoiceHeadId",
                table: "InvoiceCorrectionCases");

            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceCorrectionCases_InvoiceHeads_OriginalInvoiceHeadId",
                table: "InvoiceCorrectionCases");

            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceHeads_InvoiceHeads_OriginalInvoiceHeadId",
                table: "InvoiceHeads");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceHeads_StoreId_OrderId",
                table: "InvoiceHeads");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceCorrectionCases_StoreId",
                table: "InvoiceCorrectionCases");

            migrationBuilder.AlterColumn<string>(
                name: "OriginalInvoiceNo",
                table: "InvoiceHeads",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<byte>(
                name: "CorrectionType",
                table: "InvoiceHeads",
                type: "tinyint",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AdjustedNote",
                table: "InvoiceHeads",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AdditionalReferenceDesc",
                table: "InvoiceHeads",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<byte>(
                name: "Type",
                table: "InvoiceCorrectionCases",
                type: "tinyint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<byte>(
                name: "Status",
                table: "InvoiceCorrectionCases",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<string>(
                name: "Reason",
                table: "InvoiceCorrectionCases",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Note",
                table: "InvoiceCorrectionCases",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "LastErrorMessage",
                table: "InvoiceCorrectionCases",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "LastErrorCode",
                table: "InvoiceCorrectionCases",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AgreementDocumentNo",
                table: "InvoiceCorrectionCases",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHeads_StoreId_CorrectionType_IsDeleted",
                table: "InvoiceHeads",
                columns: new[] { "StoreId", "CorrectionType", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHeads_StoreId_OrderId",
                table: "InvoiceHeads",
                columns: new[] { "StoreId", "OrderId" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [OriginalInvoiceHeadId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHeads_StoreId_OriginalInvoiceHeadId_IsDeleted",
                table: "InvoiceHeads",
                columns: new[] { "StoreId", "OriginalInvoiceHeadId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceCorrectionCases_StoreId_CreatedAtUtc_IsDeleted",
                table: "InvoiceCorrectionCases",
                columns: new[] { "StoreId", "CreatedAtUtc", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceCorrectionCases_StoreId_NewInvoiceHeadId_IsDeleted",
                table: "InvoiceCorrectionCases",
                columns: new[] { "StoreId", "NewInvoiceHeadId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceCorrectionCases_StoreId_OriginalInvoiceHeadId_IsDeleted",
                table: "InvoiceCorrectionCases",
                columns: new[] { "StoreId", "OriginalInvoiceHeadId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceCorrectionCases_StoreId_Type_Status_IsDeleted",
                table: "InvoiceCorrectionCases",
                columns: new[] { "StoreId", "Type", "Status", "IsDeleted" });

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceCorrectionCases_InvoiceHeads_NewInvoiceHeadId",
                table: "InvoiceCorrectionCases",
                column: "NewInvoiceHeadId",
                principalTable: "InvoiceHeads",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceCorrectionCases_InvoiceHeads_OriginalInvoiceHeadId",
                table: "InvoiceCorrectionCases",
                column: "OriginalInvoiceHeadId",
                principalTable: "InvoiceHeads",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceHeads_InvoiceHeads_OriginalInvoiceHeadId",
                table: "InvoiceHeads",
                column: "OriginalInvoiceHeadId",
                principalTable: "InvoiceHeads",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceCorrectionCases_InvoiceHeads_NewInvoiceHeadId",
                table: "InvoiceCorrectionCases");

            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceCorrectionCases_InvoiceHeads_OriginalInvoiceHeadId",
                table: "InvoiceCorrectionCases");

            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceHeads_InvoiceHeads_OriginalInvoiceHeadId",
                table: "InvoiceHeads");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceHeads_StoreId_CorrectionType_IsDeleted",
                table: "InvoiceHeads");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceHeads_StoreId_OrderId",
                table: "InvoiceHeads");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceHeads_StoreId_OriginalInvoiceHeadId_IsDeleted",
                table: "InvoiceHeads");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceCorrectionCases_StoreId_CreatedAtUtc_IsDeleted",
                table: "InvoiceCorrectionCases");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceCorrectionCases_StoreId_NewInvoiceHeadId_IsDeleted",
                table: "InvoiceCorrectionCases");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceCorrectionCases_StoreId_OriginalInvoiceHeadId_IsDeleted",
                table: "InvoiceCorrectionCases");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceCorrectionCases_StoreId_Type_Status_IsDeleted",
                table: "InvoiceCorrectionCases");

            migrationBuilder.AlterColumn<string>(
                name: "OriginalInvoiceNo",
                table: "InvoiceHeads",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "CorrectionType",
                table: "InvoiceHeads",
                type: "int",
                nullable: true,
                oldClrType: typeof(byte),
                oldType: "tinyint",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AdjustedNote",
                table: "InvoiceHeads",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AdditionalReferenceDesc",
                table: "InvoiceHeads",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Type",
                table: "InvoiceCorrectionCases",
                type: "int",
                nullable: false,
                oldClrType: typeof(byte),
                oldType: "tinyint");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "InvoiceCorrectionCases",
                type: "int",
                nullable: false,
                oldClrType: typeof(byte),
                oldType: "tinyint",
                oldDefaultValue: (byte)0);

            migrationBuilder.AlterColumn<string>(
                name: "Reason",
                table: "InvoiceCorrectionCases",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "Note",
                table: "InvoiceCorrectionCases",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "LastErrorMessage",
                table: "InvoiceCorrectionCases",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "LastErrorCode",
                table: "InvoiceCorrectionCases",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AgreementDocumentNo",
                table: "InvoiceCorrectionCases",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(255)",
                oldMaxLength: 255);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHeads_StoreId_OrderId",
                table: "InvoiceHeads",
                columns: new[] { "StoreId", "OrderId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceCorrectionCases_StoreId",
                table: "InvoiceCorrectionCases",
                column: "StoreId");

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceCorrectionCases_InvoiceHeads_NewInvoiceHeadId",
                table: "InvoiceCorrectionCases",
                column: "NewInvoiceHeadId",
                principalTable: "InvoiceHeads",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceCorrectionCases_InvoiceHeads_OriginalInvoiceHeadId",
                table: "InvoiceCorrectionCases",
                column: "OriginalInvoiceHeadId",
                principalTable: "InvoiceHeads",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceHeads_InvoiceHeads_OriginalInvoiceHeadId",
                table: "InvoiceHeads",
                column: "OriginalInvoiceHeadId",
                principalTable: "InvoiceHeads",
                principalColumn: "Id");
        }
    }
}
