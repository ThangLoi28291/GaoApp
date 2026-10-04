using FluentAssertions;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Application.Services.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Invoices;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;

namespace GaoApp.Tests.Invoices;

public sealed class InvoiceInputStockRepositoryTests
{
    [Fact]
    public async Task Issue_ShouldKeepDraftAndNotCallProvider_WhenInputInvoiceStockIsInsufficient()
    {
        await using var context = CreateContext();
        var data = await SeedBaseAsync(context, requiredQuantity: 4m);
        await AddReceiptAsync(
            context,
            documentId: 101,
            lineId: 201,
            data.VariantId,
            baseQuantity: 3m,
            withInputInvoice: true);
        context.InvoiceProviderSettings.Add(new InvoiceProviderSetting
        {
            Id = 701,
            StoreId = 1,
            ProviderCode = "VIETTEL",
            BaseUrl = "https://example.test",
            Username = "user",
            Password = "protected-password",
            SupplierTaxCode = "0100000001",
            InvoiceType = "1",
            TemplateCode = "1/001",
            InvoiceSeries = "C26TAA",
            CurrencyCode = "VND",
            IsActive = true,
            RowVersion = new byte[8]
        });
        await context.SaveChangesAsync();

        var issueClient = new RecordingIssueClient();
        var service = new ViettelInvoiceIssueService(
            new InvoiceRepository(context),
            new InvoiceProviderSettingRepository(context, new TestCredentialProtector()),
            new InvoiceCorrectionRepository(context),
            new InvoiceInputStockRepository(context),
            new NoOpUnitOfWork(),
            new SuccessfulPayloadBuilder(),
            issueClient);

        var result = await service.IssueAsync(data.InvoiceHeadId);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("Invoice.InputInvoiceStockInsufficient");
        result.Error.Message.Should().Contain("khả dụng 3").And.Contain("thiếu 1");
        issueClient.CallCount.Should().Be(0);
        context.InvoiceHeads.Single(x => x.Id == data.InvoiceHeadId)
            .ProviderStatus.Should().Be(InvoiceProviderStatus.LocalDraft);
    }

    [Fact]
    public async Task Availability_ShouldCountOnlyConfirmedReceiptLinesMappedToInputInvoice()
    {
        await using var context = CreateContext();
        var data = await SeedBaseAsync(context, requiredQuantity: 4m);

        await AddReceiptAsync(
            context,
            documentId: 101,
            lineId: 201,
            data.VariantId,
            baseQuantity: 3m,
            withInputInvoice: true);
        await AddReceiptAsync(
            context,
            documentId: 102,
            lineId: 202,
            data.VariantId,
            baseQuantity: 2m,
            withInputInvoice: false);

        var repository = new InvoiceInputStockRepository(context);

        var result = await repository.GetAvailabilityAsync(data.InvoiceHeadId);

        result.IsSufficient.Should().BeFalse();
        result.Lines.Should().ContainSingle();
        result.Lines.Single().Should().BeEquivalentTo(new
        {
            WarehouseId = 11,
            ProductVariantId = data.VariantId,
            RequiredBaseQuantity = 4m,
            EligibleInboundBaseQuantity = 3m,
            CommittedOutboundBaseQuantity = 0m,
            AvailableBaseQuantity = 3m,
            ShortageBaseQuantity = 1m,
            IsSufficient = false
        });
    }

    [Fact]
    public async Task Availability_ShouldSubtractQuantityAlreadyCommittedByIssuedInvoice()
    {
        await using var context = CreateContext();
        var current = await SeedBaseAsync(context, requiredQuantity: 2m);
        await AddReceiptAsync(
            context,
            documentId: 101,
            lineId: 201,
            current.VariantId,
            baseQuantity: 3m,
            withInputInvoice: true);

        var issuedHead = new InvoiceHead
        {
            Id = 302,
            StoreId = 1,
            OrderId = current.OrderId,
            ProviderStatus = InvoiceProviderStatus.Issued,
            ProviderInvoiceNo = "00000001",
            RowVersion = new byte[8]
        };
        var issuedDetail = new InvoiceDetail
        {
            Id = 402,
            StoreId = 1,
            InvoiceHeadId = issuedHead.Id,
            OrderLineId = current.OrderLineId,
            OrderLegalEntityAllocationId = current.AllocationId,
            ProductVariantId = current.VariantId,
            ItemName = "Sản phẩm có hóa đơn",
            Quantity = 2m,
            RowVersion = new byte[8]
        };
        context.InvoiceHeads.Add(issuedHead);
        context.InvoiceDetails.Add(issuedDetail);
        await context.SaveChangesAsync();

        var repository = new InvoiceInputStockRepository(context);

        var result = await repository.GetAvailabilityAsync(current.InvoiceHeadId);

        result.IsSufficient.Should().BeFalse();
        result.Lines.Single().CommittedOutboundBaseQuantity.Should().Be(2m);
        result.Lines.Single().AvailableBaseQuantity.Should().Be(1m);
        result.Lines.Single().ShortageBaseQuantity.Should().Be(1m);
    }

    [Fact]
    public async Task Availability_LegacyDraftWithoutOrderOrAllocation_UsesUnambiguousStoreWarehouse()
    {
        await using var db = CreateContext();
        var data = await SeedBaseAsync(db, requiredQuantity: 1m);
        db.InvoiceHeads.Add(new InvoiceHead
        {
            Id = 350,
            StoreId = 1,
            LegacySourceId = 99001,
            ProviderStatus = InvoiceProviderStatus.LocalDraft,
            GrandTotal = 100_000m,
            RowVersion = new byte[8]
        });
        db.InvoiceDetails.Add(new InvoiceDetail
        {
            Id = 450,
            StoreId = 1,
            InvoiceHeadId = 350,
            ProductVariantId = data.VariantId,
            ItemName = "Sản phẩm có hóa đơn",
            Quantity = 1m,
            RowVersion = new byte[8]
        });
        await db.SaveChangesAsync();

        await AddSupplementalAsync(
            db,
            id: 99002,
            variantId: data.VariantId,
            quantityChange: 10m,
            movementType: InvoiceInputStockSupplementalMovementType.LegacyInbound,
            legacySourceKey: "legacy-default-warehouse",
            effectiveAtUtc: DateTime.UtcNow);

        var result = await new InvoiceInputStockRepository(db).GetAvailabilityAsync(350);

        result.IsSufficient.Should().BeTrue();
        result.Lines.Should().ContainSingle();
        result.Lines.Single().WarehouseId.Should().Be(11);
        result.Lines.Single().AvailableBaseQuantity.Should().Be(10m);
    }

    [Theory]
    [InlineData(100, 60, 60)]
    [InlineData(60, 100, 60)]
    [InlineData(48, 48, 48)]
    public async Task Xml_quantity_is_capped_by_mapped_receipt_and_converted_xml(decimal received, decimal xmlBase, decimal expected)
    {
        await using var db = CreateContext();
        var data = await SeedBaseAsync(db, 4);
        await AddReceiptAsync(db, 101, 201, data.VariantId, received, true);
        var evidence = await db.StockDocumentInputInvoiceDetailReconciliations.SingleAsync();
        evidence.XmlQuantity = xmlBase / 4; evidence.ConfirmedFactor = 4; evidence.DerivedBaseQuantity = xmlBase;
        await db.SaveChangesAsync();
        var result = await new InvoiceInputStockRepository(db).GetAvailabilityAsync(data.InvoiceHeadId);
        Assert.Equal(expected, Assert.Single(result.Lines).EligibleInboundBaseQuantity);
    }

    [Fact]
    public async Task Shared_xml_detail_is_not_counted_twice_across_receipts_or_warehouses()
    {
        await using var db = CreateContext();
        var data = await SeedBaseAsync(db, 4);
        await AddReceiptAsync(db, 101, 201, data.VariantId, 40, true);
        await AddReceiptAsync(db, 102, 202, data.VariantId, 40, true);
        db.Warehouses.Add(new Warehouse { Id = 12, StoreId = 1, LegalEntityId = 1, Name = "Kho 2", Code = "K2", RowVersion = new byte[8] });
        db.StockDocuments.Single(x => x.Id == 102).WarehouseId = 12;
        foreach (var x in db.StockDocumentInputInvoiceDetailReconciliations) { x.XmlQuantity = 60; x.DerivedBaseQuantity = 60; x.InputInvoiceHeadId = 1101; x.InputInvoiceDetailId = 4101; }
        db.StockDocumentInputInvoiceMaps.Single(x => x.StockDocumentId == 102).InputInvoiceHeadId = 1101;
        db.StockDocumentInputInvoiceReconciliations.Single(x => x.StockDocumentId == 102).InputInvoiceHeadId = 1101;
        db.StockDocumentLineInputInvoiceMaps.Single(x => x.StockDocumentId == 102).InputInvoiceDetailId = 4101;
        await db.SaveChangesAsync();
        var movements = await new InvoiceInputStockReadRepository(db).GetMovementsAsync(1);
        Assert.Equal(60, movements.Sum(x => x.Change));
        Assert.Equal(40, Assert.Single(movements, x => x.WarehouseId == 11).Change);
        Assert.Equal(20, Assert.Single(movements, x => x.WarehouseId == 12).Change);
        var page = InvoiceInputStockReadService.BuildPage(movements, new() { WarehouseId = 12 });
        Assert.Equal(20, Assert.Single(page.Balances).Available);
    }

    [Theory]
    [InlineData(InvoiceProviderStatus.LocalDraft, null, 0, 0)]
    [InlineData(InvoiceProviderStatus.Previewed, null, 0, 0)]
    [InlineData(InvoiceProviderStatus.Issuing, null, 0, 4)]
    [InlineData(InvoiceProviderStatus.IssueFailed, "TIMEOUT", 0, 4)]
    [InlineData(InvoiceProviderStatus.IssueFailed, "HTTP_500", 0, 4)]
    [InlineData(InvoiceProviderStatus.IssueFailed, "VALIDATION", 0, 0)]
    [InlineData(InvoiceProviderStatus.Issued, null, 4, 0)]
    [InlineData(InvoiceProviderStatus.IssuedWaitingNumber, null, 4, 0)]
    public async Task Only_issued_invoices_decrease_stock_pending_issuance_holds(InvoiceProviderStatus status, string? error, decimal issued, decimal held)
    {
        await using var db = CreateContext();
        var data = await SeedBaseAsync(db, 4);
        await AddReceiptAsync(db, 101, 201, data.VariantId, 60, true);
        var head = db.InvoiceHeads.Single(); head.ProviderStatus = status; head.LastErrorCode = error;
        await db.SaveChangesAsync();
        var rows = await new InvoiceInputStockReadRepository(db).GetMovementsAsync(1);
        var balance = Assert.Single(InvoiceInputStockReadService.BuildPage(rows, new()).Balances);
        Assert.Equal(issued, balance.Issued); Assert.Equal(held, balance.Held);
        Assert.Equal(60 - issued - held, balance.Available);
    }

    [Theory]
    [InlineData("boolean")]
    [InlineData("unconfirmed")]
    [InlineData("excluded")]
    [InlineData("foreign-store")]
    [InlineData("foreign-owner")]
    [InlineData("deleted-xml")]
    public async Task Invalid_or_unmapped_sources_never_supply_xml_stock(string reason)
    {
        await using var db = CreateContext();
        var data = await SeedBaseAsync(db, 4);
        await AddReceiptAsync(db, 101, 201, data.VariantId, 60, true);
        switch (reason)
        {
            case "boolean": db.StockDocumentLineInputInvoiceMaps.Single().InputInvoiceDetailId = null; break;
            case "unconfirmed": db.StockDocuments.Single().Status = StockDocumentStatus.Draft; break;
            case "excluded": db.StockDocumentInputInvoiceDetailReconciliations.Single().IsIgnored = true; break;
            case "foreign-store":
                Assert.Empty(await new InvoiceInputStockReadRepository(db).GetMovementsAsync(2));
                return;
            case "foreign-owner": db.InputInvoiceHeads.Single().ResolvedBuyerLegalEntityId = 2; break;
            case "deleted-xml": db.InputInvoiceHeads.Single().IsDeleted = true; break;
        }
        await db.SaveChangesAsync();
        Assert.Empty(await new InvoiceInputStockReadRepository(db).GetMovementsAsync(1));
        Assert.False((await new InvoiceInputStockRepository(db).GetAvailabilityAsync(data.InvoiceHeadId)).IsSufficient);
    }

    [Fact]
    public async Task Issuance_transition_releases_hold_and_repeated_reads_do_not_double_consume_or_depend_on_product_flag()
    {
        await using var db = CreateContext();
        var data = await SeedBaseAsync(db, 4);
        await AddReceiptAsync(db, 101, 201, data.VariantId, 60, true);
        var head = db.InvoiceHeads.Single(); head.ProviderStatus = InvoiceProviderStatus.Issuing;
        db.ProductVariants.Single().HasInputInvoice = false;
        await db.SaveChangesAsync();
        var repository = new InvoiceInputStockReadRepository(db);
        var before = Assert.Single(InvoiceInputStockReadService.BuildPage(await repository.GetMovementsAsync(1), new()).Balances);
        Assert.Equal(4, before.Held); Assert.Equal(0, before.Issued);
        head.ProviderStatus = InvoiceProviderStatus.Issued; head.ProviderInvoiceNo = "0000123"; head.IssuedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        for (var i = 0; i < 2; i++)
        {
            var after = Assert.Single(InvoiceInputStockReadService.BuildPage(await repository.GetMovementsAsync(1), new()).Balances);
            Assert.Equal(0, after.Held); Assert.Equal(4, after.Issued); Assert.Equal(56, after.Available);
        }
        Assert.Single((await new InvoiceInputStockRepository(db).GetAvailabilityAsync(data.InvoiceHeadId)).Lines);
    }

    [Fact]
    public void Ledger_filters_keep_opening_balance_and_balances_do_not_lose_other_sources()
    {
        var day = new DateTime(2026, 9, 10, 5, 0, 0, DateTimeKind.Utc);
        InvoiceInputStockMovement Row(string key, decimal delta, int days) => new() { Key = key, WarehouseId = 1, ProductVariantId = 1, ProductName = "Sữa", DateUtc = day.AddDays(days), Change = delta, Kind = delta > 0 ? "increase" : "decrease", SourceCode = key };
        InvoiceInputStockMovement[] rows = [Row("XML-1", 60, -1), Row("HD-1", -4, 0), Row("HD-2", -6, 1)];
        var ledger = InvoiceInputStockReadService.BuildPage(rows, new() { View = "ledger", FromDate = day.Date, ToDate = day.Date, Kind = "decrease" });
        Assert.Equal(60, Assert.Single(ledger.Movements).Before); Assert.Equal(56, ledger.Movements[0].After);
        var balances = InvoiceInputStockReadService.BuildPage(rows, new() { Keyword = "HD-1", Page = 999 });
        Assert.Equal(50, Assert.Single(balances.Balances).Available); Assert.Equal(1, balances.Page);
    }

    [Fact]
    public async Task Supplemental_opening_inbound_and_reconciliation_adjustment_share_running_balance()
    {
        await using var db = CreateContext();
        var data = await SeedBaseAsync(db, 4);
        var day = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        await AddSupplementalAsync(db, 9001, data.VariantId, 100m,
            InvoiceInputStockSupplementalMovementType.LegacyOpening, "OPEN-202506", day);
        await AddSupplementalAsync(db, 9002, data.VariantId, 20m,
            InvoiceInputStockSupplementalMovementType.LegacyInbound, "IN-202506-001", day.AddDays(1));
        await AddSupplementalAsync(db, 9003, data.VariantId, -30m,
            InvoiceInputStockSupplementalMovementType.LegacyReconciliationAdjustment, "REC-202506", day.AddDays(2));

        var rows = await new InvoiceInputStockReadRepository(db).GetMovementsAsync(1);
        var supplemental = rows.Where(x => x.Key.StartsWith("supplemental-", StringComparison.Ordinal))
            .OrderBy(x => x.DateUtc).ToList();

        supplemental.Select(x => x.Change).Should().Equal(100m, 20m, -30m);
        var page = InvoiceInputStockReadService.BuildPage(rows, new() { View = "ledger", PageSize = 100 });
        supplemental[0].Before.Should().Be(0m); supplemental[0].After.Should().Be(100m);
        supplemental[1].Before.Should().Be(100m); supplemental[1].After.Should().Be(120m);
        supplemental[2].Before.Should().Be(120m); supplemental[2].After.Should().Be(90m);
        var balance = Assert.Single(page.Balances.Count == 0
            ? InvoiceInputStockReadService.BuildPage(rows, new()).Balances
            : page.Balances);
        balance.Opening.Should().Be(100m);
        balance.Received.Should().Be(20m);
        balance.Issued.Should().Be(30m);
        balance.Available.Should().Be(90m);
        supplemental[0].Note.Should().Contain("Tồn đầu kỳ cũ");
        supplemental[1].Note.Should().Contain("Nhập HĐĐT lịch sử");
        supplemental[2].Note.Should().Contain("Điều chỉnh lịch sử");
    }

    [Fact]
    public async Task Legacy_outbound_is_consumed_by_ledger_and_issuance_without_physical_posting()
    {
        await using var db = CreateContext();
        var data = await SeedBaseAsync(db, 4);
        var day = new DateTime(2025, 5, 31, 17, 0, 0, DateTimeKind.Utc);
        await AddSupplementalAsync(db, 9081, data.VariantId, 10m,
            InvoiceInputStockSupplementalMovementType.LegacyOpening, "GSTORE-IIS-V1|O|1", day);
        await AddSupplementalAsync(db, 9082, data.VariantId, -8m,
            InvoiceInputStockSupplementalMovementType.LegacyOutbound, "GSTORE-IIS-V1|X|1", day.AddDays(1));
        var source = await db.InvoiceInputStockSupplementalMovements.SingleAsync(x => x.Id == 9082);
        source.LegacyOrderId = 1799381;
        source.LegacyInvoiceNumber = "C26TVK270";
        source.LegacyInvoiceSymbol = "C26TVK";
        await db.SaveChangesAsync();

        var rows = await new InvoiceInputStockReadRepository(db).GetMovementsAsync(1);
        var outbound = Assert.Single(rows, x => x.LegacySourceKey == "GSTORE-IIS-V1|X|1");
        outbound.SourceCode.Should().Contain("1799381").And.Contain("C26TVK270");
        outbound.LegacyOrderId.Should().Be("1799381");
        outbound.LegacyInvoiceNumber.Should().Be("C26TVK270");
        outbound.InvoiceHeadId.Should().BeNull("legacy IDs must not link to unrelated GaoApp invoices");
        outbound.StockDocumentId.Should().BeNull();
        outbound.OperationLabel.Should().Be("Xuất HĐĐT lịch sử");
        var search = InvoiceInputStockReadService.BuildPage(rows, new() { View = "ledger", Keyword = "1799381" });
        Assert.Single(search.Movements).LegacySourceKey.Should().Be(outbound.LegacySourceKey);
        outbound.Kind.Should().Be("decrease");
        outbound.Held.Should().Be(0m);
        outbound.Note.Should().Contain("Xuất HĐĐT lịch sử");
        var balance = Assert.Single(InvoiceInputStockReadService.BuildPage(rows, new()).Balances);
        balance.Remaining.Should().Be(2m);
        balance.Issued.Should().Be(8m);

        var availability = await new InvoiceInputStockRepository(db).GetAvailabilityAsync(data.InvoiceHeadId);
        availability.IsSufficient.Should().BeFalse();
        Assert.Single(availability.Lines).AvailableBaseQuantity.Should().Be(2m);
        var reread = await new InvoiceInputStockReadRepository(db).GetMovementsAsync(1);
        Assert.Single(InvoiceInputStockReadService.BuildPage(reread, new()).Balances)
            .Available.Should().Be(2m);
        db.InventoryTransactions.Should().BeEmpty();
        db.InventoryBalances.Should().BeEmpty();
    }

    [Fact]
    public async Task Legacy_inbound_displays_source_invoice_without_inventing_order_or_xml_link()
    {
        await using var db = CreateContext();
        var data = await SeedBaseAsync(db, 4);
        await AddSupplementalAsync(db, 9083, data.VariantId, 8m,
            InvoiceInputStockSupplementalMovementType.LegacyInbound, "GSTORE-IIS-V1|I|937641", DateTime.UtcNow);
        var source = await db.InvoiceInputStockSupplementalMovements.SingleAsync(x => x.Id == 9083);
        source.LegacyInvoiceNumber = "3819";
        source.LegacyInvoiceSymbol = "C26MNP";
        await db.SaveChangesAsync();
        var rows = await new InvoiceInputStockReadRepository(db).GetMovementsAsync(1);
        var row = Assert.Single(rows, x => x.LegacySourceKey == source.LegacySourceKey);
        row.SourceCode.Should().Be("HĐ 3819 · Ký hiệu C26MNP");
        row.OperationLabel.Should().Be("Nhập HĐĐT lịch sử");
        row.LegacyOrderId.Should().BeNull();
        row.XmlNumber.Should().BeNull();
        row.InvoiceHeadId.Should().BeNull();
        row.StockDocumentId.Should().BeNull();
        row.Change.Should().Be(8m);
    }

    [Fact]
    public async Task Supplemental_negative_final_balance_is_preserved_not_clamped()
    {
        await using var db = CreateContext();
        var data = await SeedBaseAsync(db, 4);
        var day = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        await AddSupplementalAsync(db, 9011, data.VariantId, 10m,
            InvoiceInputStockSupplementalMovementType.LegacyOpening, "OPEN-NEG", day);
        await AddSupplementalAsync(db, 9012, data.VariantId, -25m,
            InvoiceInputStockSupplementalMovementType.LegacyReconciliationAdjustment, "REC-NEG", day.AddDays(1));

        var page = InvoiceInputStockReadService.BuildPage(
            await new InvoiceInputStockReadRepository(db).GetMovementsAsync(1), new());

        var balance = Assert.Single(page.Balances);
        balance.Available.Should().Be(-15m);
        page.NegativeCount.Should().Be(1);
    }

    [Fact]
    public async Task Supplemental_store_isolation_excludes_other_store_rows()
    {
        await using var db = CreateHostContext();
        var data = await SeedBaseAsync(db, 4);
        var day = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        await AddSupplementalAsync(db, 9021, data.VariantId, 100m,
            InvoiceInputStockSupplementalMovementType.LegacyOpening, "STORE-ONE", day, storeId: 1);
        db.Stores.Add(new Store { Id = 2, Name = "store-two", SubDomain = "store-two", SubDomainNormalized = "STORE-TWO", IsActive = true, RowVersion = new byte[8] });
        await db.SaveChangesAsync();
        await AddSupplementalAsync(db, 9022, data.VariantId, 999m,
            InvoiceInputStockSupplementalMovementType.LegacyOpening, "STORE-TWO", day, storeId: 2);

        var rows = await new InvoiceInputStockReadRepository(db).GetMovementsAsync(1);

        rows.Where(x => x.Key.StartsWith("supplemental-", StringComparison.Ordinal))
            .Should().ContainSingle(x => x.SourceCode == "STORE-ONE" && x.Change == 100m);
    }

    [Fact]
    public async Task Supplemental_cross_store_warehouse_or_variant_is_ignored()
    {
        await using var db = CreateHostContext();
        var data = await SeedBaseAsync(db, 4);
        await SeedSecondStoreCatalogAsync(db);
        var day = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        await AddSupplementalAsync(db, 9031, data.VariantId, 10m,
            InvoiceInputStockSupplementalMovementType.LegacyInbound, "BAD-WAREHOUSE", day, warehouseId: 22);
        await AddSupplementalAsync(db, 9032, 32, 20m,
            InvoiceInputStockSupplementalMovementType.LegacyInbound, "BAD-VARIANT", day, warehouseId: 11);

        var rows = await new InvoiceInputStockReadRepository(db).GetMovementsAsync(1);

        rows.Should().NotContain(x => x.SourceCode == "BAD-WAREHOUSE" || x.SourceCode == "BAD-VARIANT");
    }

    [Fact]
    public async Task Supplemental_and_receipt_xml_inbound_are_summed_without_double_calculator()
    {
        await using var db = CreateContext();
        var data = await SeedBaseAsync(db, 4);
        await AddReceiptAsync(db, 101, 201, data.VariantId, 60m, true);
        await AddSupplementalAsync(db, 9041, data.VariantId, 100m,
            InvoiceInputStockSupplementalMovementType.LegacyOpening, "OPEN-WITH-XML",
            new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var rows = await new InvoiceInputStockReadRepository(db).GetMovementsAsync(1);
        var balance = Assert.Single(InvoiceInputStockReadService.BuildPage(rows, new()).Balances);

        balance.Opening.Should().Be(100m);
        balance.Received.Should().Be(60m);
        balance.Available.Should().Be(160m);
        rows.Should().ContainSingle(x => x.Key.StartsWith("xml-", StringComparison.Ordinal) && x.Change == 60m);
        rows.Should().ContainSingle(x => x.Key.StartsWith("supplemental-", StringComparison.Ordinal) && x.Change == 100m);
    }

    [Fact]
    public async Task Issue_availability_uses_supplemental_balance_and_allows_has_input_invoice_false()
    {
        await using var db = CreateContext();
        var data = await SeedBaseAsync(db, 4);
        db.ProductVariants.Single(x => x.Id == data.VariantId).HasInputInvoice = false;
        await db.SaveChangesAsync();
        await AddSupplementalAsync(db, 9051, data.VariantId, 10m,
            InvoiceInputStockSupplementalMovementType.LegacyOpening, "OPEN-AVAILABILITY",
            new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        await AddSupplementalAsync(db, 9052, data.VariantId, -3m,
            InvoiceInputStockSupplementalMovementType.LegacyReconciliationAdjustment, "REC-AVAILABILITY",
            new DateTime(2025, 6, 2, 0, 0, 0, DateTimeKind.Utc));

        var result = await new InvoiceInputStockRepository(db).GetAvailabilityAsync(data.InvoiceHeadId);

        result.IsSufficient.Should().BeTrue();
        var line = Assert.Single(result.Lines);
        line.EligibleInboundBaseQuantity.Should().Be(10m);
        line.CommittedOutboundBaseQuantity.Should().Be(3m);
        line.AvailableBaseQuantity.Should().Be(7m);
        (await new InvoiceInputStockReadRepository(db).GetMovementsAsync(1))
            .Should().ContainSingle(x => x.SourceCode == "OPEN-AVAILABILITY");
    }

    [Fact]
    public async Task Supplemental_read_and_write_do_not_create_physical_inventory_rows()
    {
        await using var db = CreateContext();
        var data = await SeedBaseAsync(db, 4);
        await AddSupplementalAsync(db, 9061, data.VariantId, 25m,
            InvoiceInputStockSupplementalMovementType.LegacyInbound, "NO-PHYSICAL",
            new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        _ = await new InvoiceInputStockReadRepository(db).GetMovementsAsync(1);

        db.InventoryTransactions.Should().BeEmpty();
        db.InventoryBalances.Should().BeEmpty();
    }

    [Fact]
    public async Task Supplemental_does_not_change_existing_issued_draft_hold_or_correction_classification()
    {
        await using var db = CreateContext();
        var data = await SeedBaseAsync(db, 4);
        await AddSupplementalAsync(db, 9071, data.VariantId, 100m,
            InvoiceInputStockSupplementalMovementType.LegacyOpening, "OPEN-CLASSIFICATION",
            new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var current = db.InvoiceHeads.Single(x => x.Id == data.InvoiceHeadId);
        current.ProviderStatus = InvoiceProviderStatus.Issuing;

        var issued = new InvoiceHead
        {
            Id = 302, StoreId = 1, OrderId = data.OrderId, LegalEntityId = 1,
            ProviderStatus = InvoiceProviderStatus.Issued, ProviderInvoiceNo = "ISSUED-302",
            IssuedAtUtc = DateTime.UtcNow, GrandTotal = 100_000m, RowVersion = new byte[8]
        };
        var replacement = new InvoiceHead
        {
            Id = 303, StoreId = 1, OrderId = data.OrderId, LegalEntityId = 1,
            ProviderStatus = InvoiceProviderStatus.Issued, ProviderInvoiceNo = "REPL-303",
            IssuedAtUtc = DateTime.UtcNow, OriginalInvoiceHeadId = 302,
            CorrectionType = InvoiceCorrectionType.Replacement,
            GrandTotal = 100_000m, RowVersion = new byte[8]
        };
        db.InvoiceHeads.AddRange(issued, replacement);
        db.InvoiceDetails.AddRange(
            new InvoiceDetail
            {
                Id = 402, StoreId = 1, InvoiceHeadId = 302, OrderLineId = data.OrderLineId,
                OrderLegalEntityAllocationId = data.AllocationId, ProductVariantId = data.VariantId,
                ItemName = "Issued", Quantity = 2m, RowVersion = new byte[8]
            },
            new InvoiceDetail
            {
                Id = 403, StoreId = 1, InvoiceHeadId = 303, OrderLineId = data.OrderLineId,
                OrderLegalEntityAllocationId = data.AllocationId, ProductVariantId = data.VariantId,
                ItemName = "Replacement", Quantity = 9m, RowVersion = new byte[8]
            });
        await db.SaveChangesAsync();

        var rows = await new InvoiceInputStockReadRepository(db).GetMovementsAsync(1);
        var page = InvoiceInputStockReadService.BuildPage(rows, new());
        var balance = Assert.Single(page.Balances);

        balance.Opening.Should().Be(100m);
        balance.Received.Should().Be(0m);
        balance.Issued.Should().Be(4m);
        balance.Held.Should().Be(4m);
        balance.Available.Should().Be(92m);
        rows.Should().NotContain(x => x.InvoiceHeadId == 303);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manager_confirmation_counts_all_posted_goods_once_when_evidence_is_current(bool accepted)
    {
        await using var db = CreateContext();
        var data = await SeedManagerConfirmedReceiptAsync(db, accepted);

        var rows = await new InvoiceInputStockReadRepository(db).GetMovementsAsync(1);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, x => Assert.StartsWith("reviewed-receipt-", x.Key));
        Assert.Equal(60m, Assert.Single(rows, x => x.ProductVariantId == data.VariantId).Change);
        var gift = Assert.Single(rows, x => x.ProductVariantId == 32);
        Assert.Equal(8m, gift.Change);
        Assert.Equal(3m, gift.UnitCost);
        Assert.Equal(24m, gift.TotalCost);
        Assert.Equal(68m, rows.Sum(x => x.Change));
        Assert.True((await new InvoiceInputStockRepository(db).GetAvailabilityAsync(data.InvoiceHeadId)).IsSufficient);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manager_confirmation_changed_evidence_falls_back_to_xml_until_confirmed_again(bool accepted)
    {
        await using var db = CreateContext();
        var data = await SeedManagerConfirmedReceiptAsync(db, accepted);
        var reconciliation = await db.StockDocumentInputInvoiceReconciliations.SingleAsync();
        reconciliation.EvidenceFingerprint = "evidence-2";
        await db.SaveChangesAsync();

        var rows = await new InvoiceInputStockReadRepository(db).GetMovementsAsync(1);
        var xml = Assert.Single(rows);
        Assert.StartsWith("xml-", xml.Key);
        Assert.Equal(40m, xml.Change);
        var availability = await new InvoiceInputStockRepository(db).GetAvailabilityAsync(data.InvoiceHeadId);
        Assert.False(availability.IsSufficient);
        Assert.Equal(40m, Assert.Single(availability.Lines).EligibleInboundBaseQuantity);
        Assert.Equal(10m, Assert.Single(availability.Lines).ShortageBaseQuantity);

        await AddManagerDecisionAsync(db, accepted);
        rows = await new InvoiceInputStockReadRepository(db).GetMovementsAsync(1);
        Assert.Equal(68m, rows.Sum(x => x.Change));
        Assert.True((await new InvoiceInputStockRepository(db).GetAvailabilityAsync(data.InvoiceHeadId)).IsSufficient);
        Assert.Equal(2, await db.InventoryTransactions.CountAsync());
    }

    [Theory]
    [InlineData(false, PurchaseReceiptAuditEventType.InputInvoiceLinked)]
    [InlineData(false, PurchaseReceiptAuditEventType.InputInvoiceUnlinked)]
    [InlineData(false, PurchaseReceiptAuditEventType.InputInvoiceRelinked)]
    [InlineData(true, PurchaseReceiptAuditEventType.InputInvoiceReconciliationAcceptanceInvalidated)]
    [InlineData(true, PurchaseReceiptAuditEventType.InputInvoiceRelinked)]
    public async Task Manager_confirmation_later_invalidation_removes_whole_receipt_credit_even_from_cache(
        bool accepted, PurchaseReceiptAuditEventType eventType)
    {
        await using var db = CreateContext();
        await SeedManagerConfirmedReceiptAsync(db, accepted);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var repository = new InvoiceInputStockReadRepository(db, cache);
        Assert.Equal(68m, (await repository.GetMovementsAsync(1)).Sum(x => x.Change));
        db.PurchaseReceiptAuditEvents.Add(new()
        {
            StoreId = 1, StockDocumentId = 101, EventType = eventType,
            ActorUserId = 1, IsSuccess = true, OccurredAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var rows = await repository.GetMovementsAsync(1);
        Assert.Equal(40m, Assert.Single(rows).Change);
        Assert.StartsWith("xml-", Assert.Single(rows).Key);
        Assert.Equal(68m, await db.InventoryTransactions.SumAsync(x => x.QuantityChange));
    }

    [Theory]
    [InlineData(false, "broken")]
    [InlineData(false, "[]")]
    [InlineData(false, "{\"MapId\":9999,\"EvidenceFingerprint\":\"evidence-1\"}")]
    [InlineData(false, "{\"MapId\":2101,\"EvidenceFingerprint\":\"old-evidence\"}")]
    [InlineData(true, "broken")]
    [InlineData(true, "[]")]
    [InlineData(true, "{\"InputInvoiceHeadId\":9999,\"EvidenceFingerprint\":\"evidence-1\"}")]
    [InlineData(true, "{\"InputInvoiceHeadId\":1101,\"EvidenceFingerprint\":\"old-evidence\"}")]
    public async Task Manager_confirmation_invalid_or_wrong_invoice_evidence_cannot_credit_whole_receipt(
        bool accepted, string evidenceJson)
    {
        await using var db = CreateContext();
        await SeedManagerConfirmedReceiptAsync(db, accepted, evidenceJson);

        var rows = await new InvoiceInputStockReadRepository(db).GetMovementsAsync(1);
        Assert.Equal(40m, Assert.Single(rows).Change);
        Assert.StartsWith("xml-", Assert.Single(rows).Key);
    }

    [Theory]
    [InlineData("receipt-unconfirmed")]
    [InlineData("receipt-deleted")]
    [InlineData("link-deleted")]
    [InlineData("xml-deleted")]
    [InlineData("reconciliation-deleted")]
    public async Task Manager_confirmation_requires_current_confirmed_receipt_and_active_invoice_context(string scenario)
    {
        await using var db = CreateContext();
        await SeedManagerConfirmedReceiptAsync(db, accepted: false);
        switch (scenario)
        {
            case "receipt-unconfirmed": db.StockDocuments.Single().Status = StockDocumentStatus.PendingApproval; break;
            case "receipt-deleted": db.StockDocuments.Single().IsDeleted = true; break;
            case "link-deleted": db.StockDocumentInputInvoiceMaps.Single().IsDeleted = true; break;
            case "xml-deleted": db.InputInvoiceHeads.Single().IsDeleted = true; break;
            case "reconciliation-deleted": db.StockDocumentInputInvoiceReconciliations.Single().IsDeleted = true; break;
        }
        await db.SaveChangesAsync();

        Assert.Empty(await new InvoiceInputStockReadRepository(db).GetMovementsAsync(1));
        Assert.Equal(2, await db.InventoryTransactions.CountAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Manager_confirmation_acceptance_requires_current_accepted_state_and_fingerprint(bool clearFingerprint)
    {
        await using var db = CreateContext();
        await SeedManagerConfirmedReceiptAsync(db, accepted: true);
        var reconciliation = await db.StockDocumentInputInvoiceReconciliations.SingleAsync();
        if (clearFingerprint) reconciliation.AcceptedEvidenceFingerprint = null;
        else reconciliation.OverallState = InputInvoiceReconciliationState.Mismatch;
        await db.SaveChangesAsync();

        Assert.Equal(40m, Assert.Single(await new InvoiceInputStockReadRepository(db).GetMovementsAsync(1)).Change);
    }

    private static async Task<SeededData> SeedManagerConfirmedReceiptAsync(
        InMemoryAppDbContext db, bool accepted, string? evidenceJson = null)
    {
        var data = await SeedBaseAsync(db, requiredQuantity: 50m);
        await AddReceiptAsync(db, 101, 201, data.VariantId, 60m, withInputInvoice: true);
        db.ProductVariants.Add(new()
        {
            Id = 32, StoreId = 1, ProductId = 1, Sku = "GIFT-32",
            ProductVariantName = "Quà chưa ghép XML", IsActive = true, RowVersion = new byte[8]
        });
        db.StockDocumentLines.Add(new()
        {
            Id = 202, StockDocumentId = 101, LineNo = 2, ProductVariantId = 32,
            Quantity = 8m, BaseQuantity = 8m, Factor = 1m,
            ProductNameSnapshot = "Quà chưa ghép XML", RowVersion = new byte[8]
        });
        db.InventoryTransactions.AddRange(
            new InventoryTransaction
            {
                Id = 1001, StoreId = 1, WarehouseId = 11, ProductVariantId = data.VariantId,
                ReferenceType = InventoryReferenceType.StockDocument, ReferenceId = "101", ReferenceLineId = 201,
                TransactionType = InventoryTransactionType.PurchaseReceipt, QuantityChange = 60m,
                UnitCostSnapshot = 10m, TotalCost = 600m, RowVersion = new byte[8]
            },
            new InventoryTransaction
            {
                Id = 1002, StoreId = 1, WarehouseId = 11, ProductVariantId = 32,
                ReferenceType = InventoryReferenceType.StockDocument, ReferenceId = "101", ReferenceLineId = 202,
                TransactionType = InventoryTransactionType.PurchaseReceipt, QuantityChange = 8m,
                UnitCostSnapshot = 3m, TotalCost = 24m, RowVersion = new byte[8]
            });
        var evidence = await db.StockDocumentInputInvoiceDetailReconciliations.SingleAsync();
        evidence.XmlQuantity = 40m;
        evidence.DerivedBaseQuantity = 40m;
        var reconciliation = await db.StockDocumentInputInvoiceReconciliations.SingleAsync();
        reconciliation.EvidenceFingerprint = "evidence-1";
        await AddManagerDecisionAsync(db, accepted, evidenceJson);
        return data;
    }

    private static async Task AddManagerDecisionAsync(InMemoryAppDbContext db, bool accepted, string? evidenceJson = null)
    {
        var reconciliation = await db.StockDocumentInputInvoiceReconciliations.SingleAsync();
        reconciliation.OverallState = accepted
            ? InputInvoiceReconciliationState.AcceptedMismatch : InputInvoiceReconciliationState.Incomplete;
        reconciliation.AcceptedEvidenceFingerprint = accepted ? reconciliation.EvidenceFingerprint : null;
        db.PurchaseReceiptAuditEvents.Add(new()
        {
            StoreId = 1, StockDocumentId = 101, ActorUserId = 1, IsSuccess = true,
            EventType = accepted ? PurchaseReceiptAuditEventType.InputInvoiceReconciliationAccepted
                : PurchaseReceiptAuditEventType.InputInvoiceFollowUpReviewed,
            OccurredAtUtc = DateTime.UtcNow,
            NewValuesJson = evidenceJson ?? (accepted
                ? JsonSerializer.Serialize(new { reconciliation.InputInvoiceHeadId, reconciliation.EvidenceFingerprint })
                : JsonSerializer.Serialize(new { MapId = reconciliation.StockDocumentInputInvoiceMapId, reconciliation.EvidenceFingerprint }))
        });
        await db.SaveChangesAsync();
    }

    private static async Task<SeededData> SeedBaseAsync(
        InMemoryAppDbContext context,
        decimal requiredQuantity)
    {
        context.Stores.Add(new Store
        {
            Id = 1,
            Name = "store-one",
            SubDomain = "store-one",
            SubDomainNormalized = "STORE-ONE",
            IsActive = true,
            RowVersion = new byte[8]
        });

        context.POSShifts.Add(new POSShift
        {
            Id = 21,
            StoreId = 1,
            TerminalId = 1,
            OpenedByUserId = 1,
            WarehouseId = 11,
            RowVersion = new byte[8]
        });

        context.Units.Add(new Unit { Id = 1, StoreId = 1, Code = "HOP", Name = "hộp", RowVersion = new byte[8] });
        context.Products.Add(new Product { Id = 1, StoreId = 1, Name = "Sữa tươi", Alias = "sua", BaseUnitId = 1, RowVersion = new byte[8] });
        context.LegalEntities.Add(new LegalEntity { Id = 1, StoreId = 1, Code = "TIEM", Name = "Tiệm", LegalName = "Tiệm", RowVersion = new byte[8] });
        context.Warehouses.Add(new Warehouse { Id = 11, StoreId = 1, Name = "Kho chính", Code = "K1", LegalEntityId = 1, RowVersion = new byte[8] });

        context.ProductVariants.Add(new ProductVariant
        {
            Id = 31,
            StoreId = 1,
            ProductId = 1,
            Sku = "INPUT-INVOICE-31",
            ProductVariantName = "Sản phẩm có hóa đơn",
            HasInputInvoice = true,
            IsActive = true,
            RowVersion = new byte[8]
        });

        context.Orders.Add(new Order
        {
            Id = 41,
            StoreId = 1,
            POSShiftId = 21,
            GrandTotal = 400_000m,
            RowVersion = new byte[8]
        });

        context.OrderLines.Add(new OrderLine
        {
            Id = 51,
            StoreId = 1,
            OrderId = 41,
            ProductId = 1,
            VariantId = 31,
            ItemName = "Sản phẩm có hóa đơn",
            Quantity = requiredQuantity,
            BaseQuantity = requiredQuantity,
            Multiplier = 1m,
            UnitPrice = 100_000m,
            LineTotal = requiredQuantity * 100_000m,
            RowVersion = new byte[8]
        });

        context.OrderLegalEntityAllocations.Add(new OrderLegalEntityAllocation
        {
            Id = 61,
            StoreId = 1,
            OrderId = 41,
            OrderLineId = 51,
            ProductVariantId = 31,
            LegalEntityId = 1,
            WarehouseId = 11,
            SalePriority = 1,
            Quantity = requiredQuantity,
            BaseQuantity = requiredQuantity,
            UnitPrice = 100_000m,
            LineTotal = requiredQuantity * 100_000m,
            NetAmount = requiredQuantity * 100_000m,
            RowVersion = new byte[8]
        });

        context.InvoiceHeads.Add(new InvoiceHead
        {
            Id = 301,
            StoreId = 1,
            OrderId = 41,
            ProviderStatus = InvoiceProviderStatus.LocalDraft,
            GrandTotal = requiredQuantity * 100_000m,
            RowVersion = new byte[8]
        });

        context.InvoiceDetails.Add(new InvoiceDetail
        {
            Id = 401,
            StoreId = 1,
            InvoiceHeadId = 301,
            OrderLineId = 51,
            OrderLegalEntityAllocationId = 61,
            ProductVariantId = 31,
            ItemName = "Sản phẩm có hóa đơn",
            Quantity = requiredQuantity,
            RowVersion = new byte[8]
        });

        await context.SaveChangesAsync();

        return new SeededData(301, 41, 51, 61, 31);
    }

    private static async Task AddReceiptAsync(
        InMemoryAppDbContext context,
        int documentId,
        int lineId,
        int variantId,
        decimal baseQuantity,
        bool withInputInvoice)
    {
        context.StockDocuments.Add(new StockDocument
        {
            Id = documentId,
            StoreId = 1,
            DocumentNo = $"PN-{documentId}",
            Type = StockDocumentType.Receipt,
            Status = StockDocumentStatus.Confirmed,
            WarehouseId = 11,
            RowVersion = new byte[8]
        });
        context.StockDocumentLines.Add(new StockDocumentLine
        {
            Id = lineId,
            StockDocumentId = documentId,
            LineNo = 1,
            ProductVariantId = variantId,
            Quantity = baseQuantity,
            Factor = 1m,
            BaseQuantity = baseQuantity,
            ProductNameSnapshot = "Sản phẩm có hóa đơn",
            RowVersion = new byte[8]
        });

        if (withInputInvoice)
        {
            var inputHeadId = documentId + 1000;
            context.InputInvoiceHeads.Add(new InputInvoiceHead
            {
                Id = inputHeadId,
                StoreId = 1,
                InvoiceNumber = $"HD-{documentId}",
                RowVersion = new byte[8]
            });
            context.StockDocumentInputInvoiceMaps.Add(new StockDocumentInputInvoiceMap
            {
                Id = documentId + 2000,
                StoreId = 1,
                StockDocumentId = documentId,
                InputInvoiceHeadId = inputHeadId,
                RowVersion = new byte[8]
            });
            context.StockDocumentLineInputInvoiceMaps.Add(new StockDocumentLineInputInvoiceMap
            {
                Id = documentId + 3000,
                StoreId = 1,
                StockDocumentId = documentId,
                StockDocumentLineId = lineId,
                UseInputInvoice = true,
                InputInvoiceDetailId = documentId + 4000,
                RowVersion = new byte[8]
            });
            context.InputInvoiceDetails.Add(new InputInvoiceDetail
            {
                Id = documentId + 4000, InputInvoiceHeadId = inputHeadId, ItemName = "Sữa", Quantity = baseQuantity, RowVersion = new byte[8]
            });
            context.StockDocumentInputInvoiceReconciliations.Add(new StockDocumentInputInvoiceReconciliation
            {
                Id = documentId + 5000, StoreId = 1, StockDocumentId = documentId, InputInvoiceHeadId = inputHeadId,
                StockDocumentInputInvoiceMapId = documentId + 2000, OverallState = InputInvoiceReconciliationState.Matched, RowVersion = new byte[8]
            });
            context.StockDocumentInputInvoiceDetailReconciliations.Add(new StockDocumentInputInvoiceDetailReconciliation
            {
                Id = documentId + 6000, StoreId = 1, StockDocumentId = documentId, InputInvoiceHeadId = inputHeadId,
                InputInvoiceDetailId = documentId + 4000, StockDocumentInputInvoiceReconciliationId = documentId + 5000,
                ProductVariantId = variantId, ConfirmedBaseUnitId = 1, ConfirmedFactor = 1,
                XmlQuantity = baseQuantity, DerivedBaseQuantity = baseQuantity, ReceiptBaseQuantity = baseQuantity, RowVersion = new byte[8]
            });
        }

        await context.SaveChangesAsync();
    }

    private static async Task AddSupplementalAsync(
        InMemoryAppDbContext context,
        int id,
        int variantId,
        decimal quantityChange,
        InvoiceInputStockSupplementalMovementType movementType,
        string legacySourceKey,
        DateTime effectiveAtUtc,
        int storeId = 1,
        int warehouseId = 11)
    {
        context.InvoiceInputStockSupplementalMovements.Add(new InvoiceInputStockSupplementalMovement
        {
            Id = id,
            StoreId = storeId,
            WarehouseId = warehouseId,
            ProductVariantId = variantId,
            EffectiveAtUtc = effectiveAtUtc,
            QuantityChange = quantityChange,
            MovementType = movementType,
            LegacySourceKey = legacySourceKey,
            SourcePeriod = "2025-06",
            Note = "synthetic-test",
            RowVersion = new byte[8]
        });
        await context.SaveChangesAsync();
    }

    private static async Task SeedSecondStoreCatalogAsync(InMemoryAppDbContext context)
    {
        context.Stores.Add(new Store
        {
            Id = 2, Name = "store-two", SubDomain = "store-two", SubDomainNormalized = "STORE-TWO",
            IsActive = true, RowVersion = new byte[8]
        });
        context.Units.Add(new Unit { Id = 2, StoreId = 2, Code = "HOP2", Name = "hộp 2", RowVersion = new byte[8] });
        context.Products.Add(new Product { Id = 2, StoreId = 2, Name = "Sữa store 2", Alias = "sua2", BaseUnitId = 2, RowVersion = new byte[8] });
        context.LegalEntities.Add(new LegalEntity { Id = 2, StoreId = 2, Code = "LE2", Name = "LE2", LegalName = "LE2", RowVersion = new byte[8] });
        context.Warehouses.Add(new Warehouse { Id = 22, StoreId = 2, LegalEntityId = 2, Name = "Kho store 2", Code = "K2", RowVersion = new byte[8] });
        context.ProductVariants.Add(new ProductVariant
        {
            Id = 32, StoreId = 2, ProductId = 2, Sku = "VAR-STORE-2",
            ProductVariantName = "Variant store 2", HasInputInvoice = true, IsActive = true, RowVersion = new byte[8]
        });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Imported_invoice_keeps_original_units_and_counts_legacy_outbound_once()
    {
        await using var db = CreateContext();
        var data = await SeedBaseAsync(db, 2m);
        var head = await db.InvoiceHeads.SingleAsync(x => x.Id == data.InvoiceHeadId);
        head.LegacySourceId = 900001;
        var detail = await db.InvoiceDetails.SingleAsync(x => x.InvoiceHeadId == head.Id);
        detail.LegacySourceId = 900002;
        detail.LegacyUnitFactor = 6m;
        detail.OrderLineId = null;
        detail.OrderLegalEntityAllocationId = null;
        var day = DateTime.UtcNow.AddDays(-2);
        await AddSupplementalAsync(db, 9091, data.VariantId, 20m,
            InvoiceInputStockSupplementalMovementType.LegacyOpening, "GSTORE-IIS-V1|O|900003", day);
        await db.SaveChangesAsync();
        var availability = await new InvoiceInputStockRepository(db).GetAvailabilityAsync(head.Id);
        Assert.Single(availability.Lines).RequiredBaseQuantity.Should().Be(12m);
        head.ProviderStatus = InvoiceProviderStatus.Issued;
        head.ProviderInvoiceNo = "C26TEST123";
        head.IssuedAtUtc = day.AddDays(1);
        await db.SaveChangesAsync();
        var rows = await new InvoiceInputStockReadRepository(db).GetMovementsAsync(1);
        Assert.Single(InvoiceInputStockReadService.BuildPage(rows, new()).Balances).Remaining.Should().Be(8m);
        await AddSupplementalAsync(db, 9092, data.VariantId, -12m,
            InvoiceInputStockSupplementalMovementType.LegacyOutbound, "GSTORE-IIS-V1|X|900002", day.AddDays(1));
        var historical = await db.InvoiceInputStockSupplementalMovements.SingleAsync(x => x.Id == 9092);
        historical.LegacyOrderId = head.LegacySourceId;
        await db.SaveChangesAsync();
        rows = await new InvoiceInputStockReadRepository(db).GetMovementsAsync(1);
        rows.Should().HaveCount(2);
        Assert.Single(rows, x => x.Change < 0).InvoiceHeadId.Should().Be(head.Id);
        Assert.Single(InvoiceInputStockReadService.BuildPage(rows, new()).Balances).Remaining.Should().Be(8m);
        detail.Quantity.Should().Be(2m);
        db.InventoryTransactions.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true, "Invoice.LegacyReadOnly")]
    [InlineData(false, "InvoiceProvider.NotConfigured")]
    public async Task Imported_draft_never_calls_provider_before_archive_and_setting_checks(bool readOnly, string error)
    {
        await using var db = CreateContext();
        var data = await SeedBaseAsync(db, 2m);
        var head = await db.InvoiceHeads.SingleAsync(x => x.Id == data.InvoiceHeadId);
        head.LegacySourceId = 1234567;
        head.LegacyReadOnly = readOnly;
        head.InvoiceProviderSettingId = null;
        if (readOnly) { head.OrderId = null; head.Order = null; head.IsLocked = true; }
        await db.SaveChangesAsync();
        var client = new RecordingIssueClient();
        var service = new ViettelInvoiceIssueService(new InvoiceRepository(db),
            new InvoiceProviderSettingRepository(db, new TestCredentialProtector()),
            new InvoiceCorrectionRepository(db), new InvoiceInputStockRepository(db),
            new NoOpUnitOfWork(), new SuccessfulPayloadBuilder(), client);
        var result = await service.IssueAsync(head.Id);
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(error);
        client.CallCount.Should().Be(0);
        head.TransactionUuid.Should().BeNull();
        (await new InvoiceRepository(db).GetInvoiceHeadWithDetailsByIdAsync(head.Id)).Should().NotBeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Imported_draft_uses_current_configuration_and_persists_it_only_when_reserving(bool enoughStock)
    {
        await using var db = CreateContext();
        var data = await SeedBaseAsync(db, 2m);
        await AddReceiptAsync(db, 101, 201, data.VariantId, enoughStock ? 3m : 1m, true);
        var head = await db.InvoiceHeads.SingleAsync(x => x.Id == data.InvoiceHeadId);
        head.LegacySourceId = 1799542;
        head.LegacySnapshotJson = "{\"SellerCode\":\"OLD-TAX\"}";
        head.InvoiceProviderSettingId = 12;
        head.SupplierTaxCode = "OLD-TAX";
        head.InvoiceType = "1";
        head.TemplateCode = "OLD-TEMPLATE";
        head.InvoiceSeries = "OLD-SERIES";
        var detail = await db.InvoiceDetails.SingleAsync(x => x.InvoiceHeadId == head.Id);
        detail.UnitPrice = 100_000m; detail.Amount = 200_000m; detail.TotalAmount = 200_000m;
        db.InvoiceProviderSettings.Add(LegacyInvoiceConfigurationTests.Setting(12));
        db.InvoiceProviderSettings.Add(LegacyInvoiceConfigurationTests.Setting());
        await db.SaveChangesAsync();
        var settings = new InvoiceProviderSettingRepository(db, new TestCredentialProtector());
        var reader = new InvoiceReadService(new InvoiceRepository(db),
            new GaoApp.Infrastructure.Repositories.Products.ProductVariantRepository(db));
        var invoiceService = InvoiceCallProxy.For<IInvoiceService>((method, args) =>
            method.Name == nameof(IInvoiceService.GetInvoiceDetailAsync)
                ? reader.GetInvoiceDetailAsync((int)args![0]!, (CancellationToken)args[1]!)
                : throw new InvalidOperationException(method.Name));
        var payloadBuilder = new ViettelInvoicePayloadBuilder(invoiceService, settings, LegacyInvoiceConfigurationTests.EmptyLogs());
        var preview = await payloadBuilder.BuildAsync(head.Id);
        Assert.True(preview.IsSuccess, preview.Error?.Message);
        Assert.Equal("NEW-TAX", preview.Value.SupplierTaxCode);
        var before = await db.InvoiceHeads.AsNoTracking().SingleAsync(x => x.Id == head.Id);
        Assert.Null(before.TransactionUuid);
        Assert.Equal("OLD-TAX", before.SupplierTaxCode);
        var client = new RecordingIssueClient
        {
            BeforeReply = async () =>
            {
                var submitted = await db.InvoiceHeads.AsNoTracking().SingleAsync(x => x.Id == head.Id);
                Assert.Equal(InvoiceProviderStatus.Issuing, submitted.ProviderStatus);
                Assert.Equal(701, submitted.InvoiceProviderSettingId);
                Assert.Equal("NEW-TAX", submitted.SupplierTaxCode);
                Assert.Equal("C26MTM", submitted.InvoiceSeries);
                Assert.False(string.IsNullOrWhiteSpace(submitted.TransactionUuid));
            }
        };
        var service = new ViettelInvoiceIssueService(new InvoiceRepository(db), settings,
            new InvoiceCorrectionRepository(db), new InvoiceInputStockRepository(db), new NoOpUnitOfWork(), payloadBuilder, client);
        var result = await service.IssueAsync(head.Id);
        var stored = await db.InvoiceHeads.AsNoTracking().SingleAsync(x => x.Id == head.Id);
        Assert.Equal("{\"SellerCode\":\"OLD-TAX\"}", stored.LegacySnapshotJson);
        if (enoughStock)
        {
            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(1, client.CallCount);
            Assert.Equal("NEW-TAX", client.SupplierTaxCode);
            Assert.Equal("https://example.test", client.BaseUrl);
            Assert.Equal(stored.TransactionUuid, client.Payload!.GeneralInvoiceInfo!.TransactionUuid);
            Assert.Equal("2/LKD3", client.Payload.GeneralInvoiceInfo.TemplateCode);
            Assert.Equal("C26MTM", client.Payload.GeneralInvoiceInfo.InvoiceSeries);
            Assert.Equal(InvoiceProviderStatus.Issued, stored.ProviderStatus);
            Assert.Equal("NEW-TAX", stored.SupplierTaxCode);
        }
        else
        {
            Assert.Equal("Invoice.InputInvoiceStockInsufficient", result.Error!.Code);
            Assert.Equal(0, client.CallCount);
            Assert.Null(stored.TransactionUuid);
            Assert.Equal("OLD-TAX", stored.SupplierTaxCode);
            Assert.Equal(12, stored.InvoiceProviderSettingId);
        }
    }

    private static InMemoryAppDbContext CreateContext()
    {
        var tenant = new TenantContext();
        tenant.SetStore(1, "store-one");
        var options = new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new InMemoryAppDbContext(options, tenant, new TestCurrentUser());
        context.VerifyRowVersionConfiguration();
        return context;
    }

    private static InMemoryAppDbContext CreateHostContext()
    {
        var tenant = new TenantContext();
        tenant.SetHostAdmin();
        var options = new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new InMemoryAppDbContext(options, tenant, new TestCurrentUser());
        context.VerifyRowVersionConfiguration();
        return context;
    }

    private sealed record SeededData(
        int InvoiceHeadId,
        int OrderId,
        int OrderLineId,
        int AllocationId,
        int VariantId);

    private sealed class TestCurrentUser : ICurrentUser
    {
        public int? UserId => 1;
        public string? UserName => "phase-22-7-input-stock-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }

    private sealed class TestCredentialProtector : IInvoiceProviderCredentialProtector
    {
        public bool IsProtected(string value) => true;
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string protectedOrLegacyPlaintext) => protectedOrLegacyPlaintext;
    }

    private sealed class SuccessfulPayloadBuilder : IViettelInvoicePayloadBuilder
    {
        public Task<Result<ViettelInvoicePayloadResultDto>> BuildForIssueAsync(
            int invoiceHeadId, InvoiceProviderSetting setting, string transactionUuid, CancellationToken ct = default)
            => BuildAsync(invoiceHeadId, ct);

        public Task<Result<ViettelInvoicePayloadResultDto>> BuildAsync(
            int invoiceHeadId,
            CancellationToken ct = default)
            => Task.FromResult(Result<ViettelInvoicePayloadResultDto>.Success(
                new ViettelInvoicePayloadResultDto
                {
                    InvoiceHeadId = invoiceHeadId,
                    StoreId = 1,
                    SupplierTaxCode = "0100000001",
                    Payload = new ViettelInvoicePayloadDto()
                }));
    }

    private sealed class RecordingIssueClient : IViettelInvoiceIssueClient
    {
        public int CallCount { get; private set; }
        public string? SupplierTaxCode { get; private set; }
        public string? BaseUrl { get; private set; }
        public ViettelInvoicePayloadDto? Payload { get; private set; }
        public Func<Task>? BeforeReply { get; init; }

        public async Task<Result<ViettelInvoiceIssueResultDto>> IssueInvoiceAsync(
            int invoiceHeadId,
            string baseUrl,
            string username,
            string password,
            InvoiceProviderAuthMode authMode,
            string supplierTaxCode,
            ViettelInvoicePayloadDto payload,
            CancellationToken ct = default)
        {
            CallCount++;
            SupplierTaxCode = supplierTaxCode; BaseUrl = baseUrl; Payload = payload;
            if (BeforeReply != null) await BeforeReply();
            return Result<ViettelInvoiceIssueResultDto>.Success(
                new ViettelInvoiceIssueResultDto
                {
                    InvoiceHeadId = invoiceHeadId,
                    IsSuccess = true,
                    InvoiceNo = "00000001"
                });
        }
    }

    private sealed class NoOpUnitOfWork : IAppUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default)
            => Task.FromResult(0);

        public Task<IAppTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Task.FromResult<IAppTransaction>(new NoOpTransaction());
    }

    private sealed class NoOpTransaction : IAppTransaction
    {
        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
