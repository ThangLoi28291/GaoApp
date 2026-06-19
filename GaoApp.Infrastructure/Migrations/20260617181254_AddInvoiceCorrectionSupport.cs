using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceCorrectionSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AdditionalReferenceDateUtc",
                table: "InvoiceHeads",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdditionalReferenceDesc",
                table: "InvoiceHeads",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdjustedNote",
                table: "InvoiceHeads",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CorrectionType",
                table: "InvoiceHeads",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OriginalInvoiceHeadId",
                table: "InvoiceHeads",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OriginalInvoiceIssuedAtUtc",
                table: "InvoiceHeads",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalInvoiceNo",
                table: "InvoiceHeads",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InvoiceCorrectionCases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OriginalInvoiceHeadId = table.Column<int>(type: "int", nullable: false),
                    NewInvoiceHeadId = table.Column<int>(type: "int", nullable: true),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AgreementDocumentNo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AgreementDateUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    IssuedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastErrorCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
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
                    table.PrimaryKey("PK_InvoiceCorrectionCases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceCorrectionCases_InvoiceHeads_NewInvoiceHeadId",
                        column: x => x.NewInvoiceHeadId,
                        principalTable: "InvoiceHeads",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InvoiceCorrectionCases_InvoiceHeads_OriginalInvoiceHeadId",
                        column: x => x.OriginalInvoiceHeadId,
                        principalTable: "InvoiceHeads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InvoiceCorrectionCases_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHeads_OriginalInvoiceHeadId",
                table: "InvoiceHeads",
                column: "OriginalInvoiceHeadId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceCorrectionCases_NewInvoiceHeadId",
                table: "InvoiceCorrectionCases",
                column: "NewInvoiceHeadId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceCorrectionCases_OriginalInvoiceHeadId",
                table: "InvoiceCorrectionCases",
                column: "OriginalInvoiceHeadId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceCorrectionCases_StoreId",
                table: "InvoiceCorrectionCases",
                column: "StoreId");

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceHeads_InvoiceHeads_OriginalInvoiceHeadId",
                table: "InvoiceHeads",
                column: "OriginalInvoiceHeadId",
                principalTable: "InvoiceHeads",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceHeads_InvoiceHeads_OriginalInvoiceHeadId",
                table: "InvoiceHeads");

            migrationBuilder.DropTable(
                name: "InvoiceCorrectionCases");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceHeads_OriginalInvoiceHeadId",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "AdditionalReferenceDateUtc",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "AdditionalReferenceDesc",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "AdjustedNote",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "CorrectionType",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "OriginalInvoiceHeadId",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "OriginalInvoiceIssuedAtUtc",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "OriginalInvoiceNo",
                table: "InvoiceHeads");
        }
    }
}
