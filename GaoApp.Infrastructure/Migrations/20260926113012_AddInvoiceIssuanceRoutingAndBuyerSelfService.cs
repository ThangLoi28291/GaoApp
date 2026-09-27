using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceIssuanceRoutingAndBuyerSelfService : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "InvoiceIssuanceRoute",
                table: "Orders",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<DateTime>(
                name: "InvoiceIssuanceRouteSelectedAtUtc",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InvoiceIssuanceRouteSelectedByUserId",
                table: "Orders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastIssuanceRelevantChangeAtUtc",
                table: "InvoiceHeads",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerCitizenId",
                table: "InvoiceHeads",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InvoiceBuyerSelfServiceRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
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
                    table.PrimaryKey(
                        "PK_InvoiceBuyerSelfServiceRequests",
                        x => x.Id);

                    table.ForeignKey(
                        name: "FK_InvoiceBuyerSelfServiceRequests_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);

                    table.ForeignKey(
                        name: "FK_InvoiceBuyerSelfServiceRequests_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_StoreId_InvoiceIssuanceRoute_CompletedAtUtc_IsDeleted",
                table: "Orders",
                columns: new[]
                {
                    "StoreId",
                    "InvoiceIssuanceRoute",
                    "CompletedAtUtc",
                    "IsDeleted"
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHeads_StoreId_LastIssuanceRelevantChangeAtUtc_IsDeleted",
                table: "InvoiceHeads",
                columns: new[]
                {
                    "StoreId",
                    "LastIssuanceRelevantChangeAtUtc",
                    "IsDeleted"
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutoInvoiceOperationSources_StoreId_InvoiceHeadId_Status_IsDeleted",
                table: "AutoInvoiceOperationSources",
                columns: new[]
                {
                    "StoreId",
                    "InvoiceHeadId",
                    "Status",
                    "IsDeleted"
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceBuyerSelfServiceRequests_OrderId",
                table: "InvoiceBuyerSelfServiceRequests",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceBuyerSelfServiceRequests_StoreId_ExpiresAtUtc_IsDeleted",
                table: "InvoiceBuyerSelfServiceRequests",
                columns: new[]
                {
                    "StoreId",
                    "ExpiresAtUtc",
                    "IsDeleted"
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceBuyerSelfServiceRequests_StoreId_OrderId_IsDeleted",
                table: "InvoiceBuyerSelfServiceRequests",
                columns: new[]
                {
                    "StoreId",
                    "OrderId",
                    "IsDeleted"
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceBuyerSelfServiceRequests_TokenHash",
                table: "InvoiceBuyerSelfServiceRequests",
                column: "TokenHash",
                unique: true);

            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1
                    FROM [dbo].[Orders] AS [o]
                    WHERE [o].[IsDeleted] = 0
                      AND [o].[Status] = 2
                      AND EXISTS (
                          SELECT 1
                          FROM [dbo].[InvoiceHeads] AS [h]
                          WHERE [h].[StoreId] = [o].[StoreId]
                            AND [h].[OrderId] = [o].[Id]
                            AND [h].[IsDeleted] = 0
                            AND [h].[IsAutoInvoiceGroup] = 0
                            AND [h].[OriginalInvoiceHeadId] IS NULL
                      )
                      AND NOT EXISTS (
                          SELECT 1
                          FROM [dbo].[InvoiceHeads] AS [hUnsafe]
                          WHERE [hUnsafe].[StoreId] = [o].[StoreId]
                            AND [hUnsafe].[OrderId] = [o].[Id]
                            AND [hUnsafe].[IsDeleted] = 0
                            AND [hUnsafe].[IsAutoInvoiceGroup] = 0
                            AND [hUnsafe].[OriginalInvoiceHeadId] IS NULL
                            AND [hUnsafe].[ProviderStatus] NOT IN (0, 1, 2)
                      )
                      AND EXISTS (
                          SELECT 1
                          FROM [dbo].[InvoiceHeads] AS [hAuto]
                          WHERE [hAuto].[StoreId] = [o].[StoreId]
                            AND [hAuto].[OrderId] = [o].[Id]
                            AND [hAuto].[IsDeleted] = 0
                            AND [hAuto].[IsAutoInvoiceGroup] = 0
                            AND [hAuto].[OriginalInvoiceHeadId] IS NULL
                            AND [hAuto].[BuyerType] = N'NoInvoice'
                      )
                      AND EXISTS (
                          SELECT 1
                          FROM [dbo].[InvoiceHeads] AS [hManual]
                          WHERE [hManual].[StoreId] = [o].[StoreId]
                            AND [hManual].[OrderId] = [o].[Id]
                            AND [hManual].[IsDeleted] = 0
                            AND [hManual].[IsAutoInvoiceGroup] = 0
                            AND [hManual].[OriginalInvoiceHeadId] IS NULL
                            AND [hManual].[BuyerType] IN (N'Individual', N'Business')
                      )
                )
                THROW 55410, 'Ambiguous unissued invoice buyer routes must be resolved before migration.', 1;

                UPDATE [o]
                SET [o].[InvoiceIssuanceRoute] = 1
                FROM [dbo].[Orders] AS [o]
                WHERE [o].[IsDeleted] = 0
                  AND [o].[Status] = 2
                  AND [o].[InvoiceIssuanceRoute] = 0
                  AND EXISTS (
                      SELECT 1
                      FROM [dbo].[InvoiceHeads] AS [h]
                      WHERE [h].[StoreId] = [o].[StoreId]
                        AND [h].[OrderId] = [o].[Id]
                        AND [h].[IsDeleted] = 0
                        AND [h].[IsAutoInvoiceGroup] = 0
                        AND [h].[OriginalInvoiceHeadId] IS NULL
                  )
                  AND NOT EXISTS (
                      SELECT 1
                      FROM [dbo].[InvoiceHeads] AS [h]
                      WHERE [h].[StoreId] = [o].[StoreId]
                        AND [h].[OrderId] = [o].[Id]
                        AND [h].[IsDeleted] = 0
                        AND [h].[IsAutoInvoiceGroup] = 0
                        AND [h].[OriginalInvoiceHeadId] IS NULL
                        AND (
                            [h].[ProviderStatus] NOT IN (0, 1, 2)
                            OR [h].[BuyerType] IS NULL
                            OR [h].[BuyerType] <> N'NoInvoice'
                        )
                  );

                UPDATE [o]
                SET [o].[InvoiceIssuanceRoute] = 2
                FROM [dbo].[Orders] AS [o]
                WHERE [o].[IsDeleted] = 0
                  AND [o].[Status] = 2
                  AND [o].[InvoiceIssuanceRoute] = 0
                  AND EXISTS (
                      SELECT 1
                      FROM [dbo].[InvoiceHeads] AS [h]
                      WHERE [h].[StoreId] = [o].[StoreId]
                        AND [h].[OrderId] = [o].[Id]
                        AND [h].[IsDeleted] = 0
                        AND [h].[IsAutoInvoiceGroup] = 0
                        AND [h].[OriginalInvoiceHeadId] IS NULL
                  )
                  AND NOT EXISTS (
                      SELECT 1
                      FROM [dbo].[InvoiceHeads] AS [h]
                      WHERE [h].[StoreId] = [o].[StoreId]
                        AND [h].[OrderId] = [o].[Id]
                        AND [h].[IsDeleted] = 0
                        AND [h].[IsAutoInvoiceGroup] = 0
                        AND [h].[OriginalInvoiceHeadId] IS NULL
                        AND (
                            [h].[ProviderStatus] NOT IN (0, 1, 2)
                            OR [h].[BuyerType] IS NULL
                            OR [h].[BuyerType] NOT IN (N'Individual', N'Business')
                        )
                  );

                UPDATE [h]
                SET [h].[LastIssuanceRelevantChangeAtUtc] =
                    CASE
                        WHEN [latestReturn].[LatestCompletedReturnAtUtc] IS NOT NULL
                             AND (
                                 [o].[CompletedAtUtc] IS NULL
                                 OR [latestReturn].[LatestCompletedReturnAtUtc] > [o].[CompletedAtUtc]
                             )
                            THEN [latestReturn].[LatestCompletedReturnAtUtc]
                        ELSE [o].[CompletedAtUtc]
                    END
                FROM [dbo].[InvoiceHeads] AS [h]
                INNER JOIN [dbo].[Orders] AS [o]
                    ON [o].[Id] = [h].[OrderId]
                   AND [o].[StoreId] = [h].[StoreId]
                OUTER APPLY (
                    SELECT MAX([sr].[CompletedAtUtc]) AS [LatestCompletedReturnAtUtc]
                    FROM [dbo].[SalesReturns] AS [sr]
                    WHERE [sr].[StoreId] = [o].[StoreId]
                      AND [sr].[OrderId] = [o].[Id]
                      AND [sr].[IsDeleted] = 0
                      AND [sr].[Status] = 1
                      AND [sr].[CompletedAtUtc] IS NOT NULL
                ) AS [latestReturn]
                WHERE [h].[IsDeleted] = 0
                  AND [h].[IsAutoInvoiceGroup] = 0
                  AND [h].[OriginalInvoiceHeadId] IS NULL
                  AND [h].[OrderId] IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1
                    FROM [dbo].[InvoiceBuyerSelfServiceRequests]
                )
                OR EXISTS (
                    SELECT 1
                    FROM [dbo].[Orders]
                    WHERE [InvoiceIssuanceRouteSelectedAtUtc] IS NOT NULL
                )
                THROW 55411, 'Cannot downgrade invoice issuance routing after feature business evidence exists.', 1;
                """);

            migrationBuilder.DropTable(
                name: "InvoiceBuyerSelfServiceRequests");

            migrationBuilder.DropIndex(
                name: "IX_AutoInvoiceOperationSources_StoreId_InvoiceHeadId_Status_IsDeleted",
                table: "AutoInvoiceOperationSources");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceHeads_StoreId_LastIssuanceRelevantChangeAtUtc_IsDeleted",
                table: "InvoiceHeads");

            migrationBuilder.DropIndex(
                name: "IX_Orders_StoreId_InvoiceIssuanceRoute_CompletedAtUtc_IsDeleted",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "BuyerCitizenId",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "LastIssuanceRelevantChangeAtUtc",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "InvoiceIssuanceRoute",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "InvoiceIssuanceRouteSelectedAtUtc",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "InvoiceIssuanceRouteSelectedByUserId",
                table: "Orders");
        }
    }
}
