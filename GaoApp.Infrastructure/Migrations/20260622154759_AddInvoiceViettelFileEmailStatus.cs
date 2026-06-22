using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceViettelFileEmailStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EmailSendCount",
                table: "InvoiceHeads",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmailSentAtUtc",
                table: "InvoiceHeads",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EmailStatus",
                table: "InvoiceHeads",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LastEmailErrorMessage",
                table: "InvoiceHeads",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastEmailTo",
                table: "InvoiceHeads",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OfficialPdfDownloadedAtUtc",
                table: "InvoiceHeads",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OfficialPdfFileName",
                table: "InvoiceHeads",
                type: "nvarchar(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OfficialPdfStatus",
                table: "InvoiceHeads",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "OfficialZipXmlDownloadedAtUtc",
                table: "InvoiceHeads",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OfficialZipXmlFileName",
                table: "InvoiceHeads",
                type: "nvarchar(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OfficialZipXmlStatus",
                table: "InvoiceHeads",
                type: "int",
                nullable: false,
                defaultValue: 0);
            migrationBuilder.Sql(@"
UPDATE InvoiceHeads
SET
    OfficialPdfStatus = 1,
    OfficialPdfDownloadedAtUtc = COALESCE(LastSyncedAtUtc, IssuedAtUtc),
    OfficialPdfFileName = RIGHT(PdfFilePath, CHARINDEX('\', REVERSE(PdfFilePath) + '\') - 1)
WHERE PdfFilePath IS NOT NULL
  AND LTRIM(RTRIM(PdfFilePath)) <> '';
");

            migrationBuilder.Sql(@"
UPDATE InvoiceHeads
SET
    OfficialZipXmlStatus = 1,
    OfficialZipXmlDownloadedAtUtc = COALESCE(LastSyncedAtUtc, IssuedAtUtc),
    OfficialZipXmlFileName = RIGHT(ZipFilePath, CHARINDEX('\', REVERSE(ZipFilePath) + '\') - 1)
WHERE ZipFilePath IS NOT NULL
  AND LTRIM(RTRIM(ZipFilePath)) <> '';
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmailSendCount",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "EmailSentAtUtc",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "EmailStatus",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "LastEmailErrorMessage",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "LastEmailTo",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "OfficialPdfDownloadedAtUtc",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "OfficialPdfFileName",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "OfficialPdfStatus",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "OfficialZipXmlDownloadedAtUtc",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "OfficialZipXmlFileName",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "OfficialZipXmlStatus",
                table: "InvoiceHeads");
        }
    }
}
