using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInputInvoiceLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InputInvoiceLibraryEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdentityHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    XmlHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DocumentKey = table.Column<string>(type: "nvarchar(350)", maxLength: 350, nullable: false),
                    SellerTaxCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SellerName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    BuyerTaxCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    BuyerName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Series = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    InvoiceDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BeforeTax = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Tax = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    InvoiceKind = table.Column<int>(type: "int", nullable: false),
                    RelatedInvoice = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    HasPdf = table.Column<bool>(type: "bit", nullable: false),
                    ReviewStatus = table.Column<int>(type: "int", nullable: false),
                    ReviewNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedBy = table.Column<int>(type: "int", nullable: true),
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
                    table.PrimaryKey("PK_InputInvoiceLibraryEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InputInvoiceLibraryEntries_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "InputInvoiceLibraryReviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InputInvoiceLibraryEntryId = table.Column<int>(type: "int", nullable: false),
                    PreviousStatus = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ActorId = table.Column<int>(type: "int", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_InputInvoiceLibraryReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InputInvoiceLibraryReviews_InputInvoiceLibraryEntries_InputInvoiceLibraryEntryId",
                        column: x => x.InputInvoiceLibraryEntryId,
                        principalTable: "InputInvoiceLibraryEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InputInvoiceLibraryReviews_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_InputInvoiceLibraryEntries_StoreId_IdentityHash",
                table: "InputInvoiceLibraryEntries",
                columns: new[] { "StoreId", "IdentityHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InputInvoiceLibraryEntries_StoreId_InvoiceDate_Id",
                table: "InputInvoiceLibraryEntries",
                columns: new[] { "StoreId", "InvoiceDate", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_InputInvoiceLibraryReviews_InputInvoiceLibraryEntryId",
                table: "InputInvoiceLibraryReviews",
                column: "InputInvoiceLibraryEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_InputInvoiceLibraryReviews_StoreId",
                table: "InputInvoiceLibraryReviews",
                column: "StoreId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InputInvoiceLibraryReviews");

            migrationBuilder.DropTable(
                name: "InputInvoiceLibraryEntries");
        }
    }
}
