using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260817150000_AddInputInvoiceIdentityUniqueness")]
public sealed class AddInputInvoiceIdentityUniqueness : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "InvoiceIdentityDate",
            table: "InputInvoiceHead",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "NormalizedInvoiceNumber",
            table: "InputInvoiceHead",
            type: "nvarchar(50)",
            maxLength: 50,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "NormalizedInvoiceSeries",
            table: "InputInvoiceHead",
            type: "nvarchar(50)",
            maxLength: 50,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "NormalizedSellerTaxCode",
            table: "InputInvoiceHead",
            type: "nvarchar(50)",
            maxLength: 50,
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE [dbo].[InputInvoiceHead]
            SET [NormalizedSellerTaxCode] = NULLIF(UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM([SellerTaxCode])), N' ', N''), N'.', N''), N'-', N''), NCHAR(9), N''), NCHAR(13), N''), NCHAR(10), N'')), N''),
                [NormalizedInvoiceSeries] = NULLIF(UPPER(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM([InvoiceSeries])), N' ', N''), NCHAR(9), N''), NCHAR(13), N''), NCHAR(10), N'')), N''),
                [NormalizedInvoiceNumber] = NULLIF(UPPER(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM([InvoiceNumber])), N' ', N''), NCHAR(9), N''), NCHAR(13), N''), NCHAR(10), N'')), N''),
                [InvoiceIdentityDate] = CONVERT(date, [InvoiceDate])
            WHERE [IsDeleted] = 0;
            """);

        migrationBuilder.Sql(
            """
            IF EXISTS
            (
                SELECT 1
                FROM [dbo].[InputInvoiceHead]
                WHERE [IsDeleted] = 0
                  AND [NormalizedSellerTaxCode] IS NOT NULL
                  AND [NormalizedInvoiceSeries] IS NOT NULL
                  AND [NormalizedInvoiceNumber] IS NOT NULL
                  AND [InvoiceIdentityDate] IS NOT NULL
                GROUP BY [StoreId], [NormalizedSellerTaxCode], [NormalizedInvoiceSeries], [NormalizedInvoiceNumber], [InvoiceIdentityDate]
                HAVING COUNT_BIG(*) > 1
            )
                THROW 51001, 'Duplicate active input-invoice business identities must be resolved before migration.', 1;

            IF EXISTS
            (
                SELECT 1
                FROM [dbo].[InputInvoiceHead]
                WHERE [IsDeleted] = 0
                  AND [XmlHash] IS NOT NULL
                GROUP BY [StoreId], [XmlHash]
                HAVING COUNT_BIG(*) > 1
            )
                THROW 51002, 'Duplicate active input-invoice XML hashes must be resolved before migration.', 1;
            """);

        migrationBuilder.CreateIndex(
            name: "UX_InputInvoiceHead_StoreId_BusinessIdentity_Active",
            table: "InputInvoiceHead",
            columns: new[]
            {
                "StoreId",
                "NormalizedSellerTaxCode",
                "NormalizedInvoiceSeries",
                "NormalizedInvoiceNumber",
                "InvoiceIdentityDate"
            },
            unique: true,
            filter: "[NormalizedSellerTaxCode] IS NOT NULL AND [NormalizedInvoiceSeries] IS NOT NULL AND [NormalizedInvoiceNumber] IS NOT NULL AND [InvoiceIdentityDate] IS NOT NULL AND [IsDeleted] = 0");

        migrationBuilder.CreateIndex(
            name: "UX_InputInvoiceHead_StoreId_XmlHash_Active",
            table: "InputInvoiceHead",
            columns: new[] { "StoreId", "XmlHash" },
            unique: true,
            filter: "[XmlHash] IS NOT NULL AND [IsDeleted] = 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "UX_InputInvoiceHead_StoreId_BusinessIdentity_Active",
            table: "InputInvoiceHead");

        migrationBuilder.DropIndex(
            name: "UX_InputInvoiceHead_StoreId_XmlHash_Active",
            table: "InputInvoiceHead");

        migrationBuilder.DropColumn(
            name: "InvoiceIdentityDate",
            table: "InputInvoiceHead");

        migrationBuilder.DropColumn(
            name: "NormalizedInvoiceNumber",
            table: "InputInvoiceHead");

        migrationBuilder.DropColumn(
            name: "NormalizedInvoiceSeries",
            table: "InputInvoiceHead");

        migrationBuilder.DropColumn(
            name: "NormalizedSellerTaxCode",
            table: "InputInvoiceHead");
    }
}
