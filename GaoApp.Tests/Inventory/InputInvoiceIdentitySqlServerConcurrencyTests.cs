using FluentAssertions;
using System.Security.Cryptography;
using System.Text;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Inventory.InputInvoices;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Tests.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GaoApp.Tests.Inventory;

[Collection("R1FinalDatabasePreflight")]
public sealed class InputInvoiceIdentitySqlServerConcurrencyTests
{
    [Fact]
    public async Task Concurrent_same_receipt_upload_should_be_fully_idempotent()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();

        var seed = await database.SeedInventoryCatalogAsync();
        var receiptId = await SeedReceiptAsync(database, seed);

        var request = Request(
            receiptId,
            "concurrent.xml",
            BuildXml());

        await using var firstDb =
            CreateContext(database, seed.StoreId);

        await using var secondDb =
            CreateContext(database, seed.StoreId);

        var firstService = new InputInvoiceXmlService(
            new InputInvoiceRepository(firstDb));

        var secondService = new InputInvoiceXmlService(
            new InputInvoiceRepository(secondDb));

        var results = await Task.WhenAll(
            firstService.UploadXmlAsync(
                seed.StoreId,
                request),
            secondService.UploadXmlAsync(
                seed.StoreId,
                request));

        results
            .Select(x => x.InputInvoiceHeadId)
            .Distinct()
            .Should().ContainSingle();

        results.Count(x => x.IsExistingInvoice)
            .Should().Be(1);

        await using var verificationDb =
            CreateContext(database, seed.StoreId);

        (await verificationDb.InputInvoiceHeads.CountAsync())
            .Should().Be(1);

        (await verificationDb.InputInvoiceDetails.CountAsync())
            .Should().Be(1);

        (await verificationDb.StockDocumentInputInvoiceMaps.CountAsync())
            .Should().Be(1);

        (await verificationDb
                .StockDocumentLineInputInvoiceMaps
                .CountAsync())
            .Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_business_identity_insert_should_return_committed_winner()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();

        var seed = await database.SeedInventoryCatalogAsync();

        await using var firstDb =
            CreateContext(database, seed.StoreId);

        await using var secondDb =
            CreateContext(database, seed.StoreId);

        var firstRepository =
            new InputInvoiceRepository(firstDb);

        var secondRepository =
            new InputInvoiceRepository(secondDb);

        var firstCandidate =
            NewCandidate(seed.StoreId, "HASH-A");

        var secondCandidate =
            NewCandidate(seed.StoreId, "HASH-B");

        await using var firstTransaction =
            await firstDb.Database.BeginTransactionAsync();

        var first = await firstRepository
            .ResolveInputInvoiceAsync(firstCandidate);

        first.IsExisting.Should().BeFalse();

        var secondTask = secondRepository
            .ResolveInputInvoiceAsync(secondCandidate);

        await Task.Delay(250);

        secondTask.IsCompleted.Should().BeFalse(
            "the unique index must serialize an uncommitted competing identity");

        await firstTransaction.CommitAsync();

        var second = await secondTask
            .WaitAsync(TimeSpan.FromSeconds(15));

        second.IsExisting.Should().BeTrue();
        second.Invoice.Id.Should().Be(first.Invoice.Id);
        second.Invoice.XmlHash.Should().Be("HASH-A");

        await using var verificationDb =
            CreateContext(database, seed.StoreId);

        (await verificationDb.InputInvoiceHeads.CountAsync())
            .Should().Be(1);

        (await verificationDb.InputInvoiceDetails.CountAsync())
            .Should().Be(1);
    }

    [Fact]
    public async Task Second_line_map_unique_conflict_should_accept_completed_durable_state()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();

        var seed = await database.SeedInventoryCatalogAsync();
        var receiptId = await SeedReceiptAsync(database, seed);

        int receiptLineId;

        await using (var lookupDb =
                     CreateContext(database, seed.StoreId))
        {
            receiptLineId = await lookupDb.StockDocumentLines
                .Where(x =>
                    x.StockDocumentId == receiptId
                    && !x.IsDeleted)
                .Select(x => x.Id)
                .SingleAsync();
        }

        var interceptor =
            new CompleteLineMapDuringBothSaveAttemptsInterceptor(
                database,
                seed.StoreId,
                receiptId,
                receiptLineId);

        await using var targetDb =
            database.CreateTenantContext(
                seed.StoreId,
                interceptor);

        var repository =
            new InputInvoiceRepository(targetDb);

        Func<Task> action = () =>
            repository.AddMissingLineMapsAsync(
                seed.StoreId,
                receiptId);

        await action.Should().NotThrowAsync();

        interceptor.SaveAttempts.Should().Be(2);
        interceptor.FailedSaveAttempts.Should().Be(2);

        await using var verificationDb =
            CreateContext(database, seed.StoreId);

        var persistedMaps = await verificationDb
            .StockDocumentLineInputInvoiceMaps
            .Where(x =>
                x.StoreId == seed.StoreId
                && x.StockDocumentId == receiptId)
            .ToListAsync();

        persistedMaps.Should().ContainSingle();

        persistedMaps.Single()
            .StockDocumentLineId
            .Should().Be(receiptLineId);
    }

    [Fact]
    public async Task Four_concurrent_same_receipt_uploads_should_converge_without_false_failure()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();

        var seed = await database.SeedInventoryCatalogAsync();
        var receiptId = await SeedReceiptAsync(database, seed);

        var request = Request(
            receiptId,
            "four-writers.xml",
            BuildXml());

        await using var firstDb =
            CreateContext(database, seed.StoreId);

        await using var secondDb =
            CreateContext(database, seed.StoreId);

        await using var thirdDb =
            CreateContext(database, seed.StoreId);

        await using var fourthDb =
            CreateContext(database, seed.StoreId);

        var services = new[]
        {
            new InputInvoiceXmlService(
                new InputInvoiceRepository(firstDb)),

            new InputInvoiceXmlService(
                new InputInvoiceRepository(secondDb)),

            new InputInvoiceXmlService(
                new InputInvoiceRepository(thirdDb)),

            new InputInvoiceXmlService(
                new InputInvoiceRepository(fourthDb))
        };

        var results = await Task.WhenAll(
            services.Select(service =>
                service.UploadXmlAsync(
                    seed.StoreId,
                    request)));

        results
            .Select(x => x.InputInvoiceHeadId)
            .Distinct()
            .Should().ContainSingle();

        results.Count(x => !x.IsExistingInvoice)
            .Should().Be(1);

        await using var verificationDb =
            CreateContext(database, seed.StoreId);

        (await verificationDb.InputInvoiceHeads.CountAsync())
            .Should().Be(1);

        (await verificationDb.InputInvoiceDetails.CountAsync())
            .Should().Be(1);

        (await verificationDb.StockDocumentInputInvoiceMaps.CountAsync())
            .Should().Be(1);

        (await verificationDb
                .StockDocumentLineInputInvoiceMaps
                .CountAsync())
            .Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_different_serializations_same_identity_different_receipts_should_converge_to_one_invoice()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();

        var seed = await database.SeedInventoryCatalogAsync();

        var firstReceiptId =
            await SeedReceiptAsync(database, seed);

        var secondReceiptId =
            await SeedReceiptAsync(database, seed);

        var firstBytes = BuildXml(
            sellerTaxCode: "031.277.0607",
            invoiceSeries: " c26 taa ",
            invoiceNumber: "9 001",
            marker: "first");

        var secondBytes = BuildXml(
            sellerTaxCode: "0312770607",
            invoiceSeries: "C26TAA",
            invoiceNumber: "9001",
            marker: "second");

        await using var firstDb =
            CreateContext(database, seed.StoreId);

        await using var secondDb =
            CreateContext(database, seed.StoreId);

        var firstService = new InputInvoiceXmlService(
            new InputInvoiceRepository(firstDb));

        var secondService = new InputInvoiceXmlService(
            new InputInvoiceRepository(secondDb));

        var results = await Task.WhenAll(
            firstService.UploadXmlAsync(
                seed.StoreId,
                Request(
                    firstReceiptId,
                    "first-serialization.xml",
                    firstBytes)),

            secondService.UploadXmlAsync(
                seed.StoreId,
                Request(
                    secondReceiptId,
                    "second-serialization.xml",
                    secondBytes)));

        results
            .Select(x => x.InputInvoiceHeadId)
            .Distinct()
            .Should().ContainSingle();

        results.Count(x => !x.IsExistingInvoice)
            .Should().Be(1);

        await using var verificationDb =
            CreateContext(database, seed.StoreId);

        (await verificationDb.InputInvoiceHeads.CountAsync())
            .Should().Be(1);

        (await verificationDb.InputInvoiceDetails.CountAsync())
            .Should().Be(1);

        (await verificationDb.StockDocumentInputInvoiceMaps.CountAsync())
            .Should().Be(2);

        (await verificationDb
                .StockDocumentLineInputInvoiceMaps
                .CountAsync())
            .Should().Be(2);

        var persisted = await verificationDb
            .InputInvoiceHeads
            .SingleAsync();

        var firstHash = Convert.ToHexString(
            SHA256.HashData(firstBytes));

        var secondHash = Convert.ToHexString(
            SHA256.HashData(secondBytes));

        persisted.XmlHash.Should().BeOneOf(
            firstHash,
            secondHash);

        persisted.SellerName.Should().BeOneOf(
            "Seller first",
            "Seller second");
    }

    [Fact]
    public async Task Same_identity_and_hash_in_different_stores_should_create_independent_invoices()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();

        var firstSeed =
            await database.SeedInventoryCatalogAsync();

        var secondSeed =
            await database.SeedInventoryCatalogAsync();

        var firstReceiptId =
            await SeedReceiptAsync(database, firstSeed);

        var secondReceiptId =
            await SeedReceiptAsync(database, secondSeed);

        var bytes = BuildXml(
            sellerTaxCode: "0312770607",
            invoiceSeries: "C26TAA",
            invoiceNumber: "9001",
            marker: "same-file");

        await using var firstDb =
            CreateContext(database, firstSeed.StoreId);

        await using var secondDb =
            CreateContext(database, secondSeed.StoreId);

        var firstService = new InputInvoiceXmlService(
            new InputInvoiceRepository(firstDb));

        var secondService = new InputInvoiceXmlService(
            new InputInvoiceRepository(secondDb));

        var firstResult = await firstService.UploadXmlAsync(
            firstSeed.StoreId,
            Request(
                firstReceiptId,
                "same-file.xml",
                bytes));

        var secondResult = await secondService.UploadXmlAsync(
            secondSeed.StoreId,
            Request(
                secondReceiptId,
                "same-file.xml",
                bytes));

        firstResult.IsExistingInvoice.Should().BeFalse();
        secondResult.IsExistingInvoice.Should().BeFalse();

        firstResult.InputInvoiceHeadId
            .Should().NotBe(secondResult.InputInvoiceHeadId);

        await using var verificationDb =
            database.CreateHostContext();

        var heads = await verificationDb
            .InputInvoiceHeads
            .IgnoreQueryFilters()
            .OrderBy(x => x.StoreId)
            .ToListAsync();

        heads.Should().HaveCount(2);

        heads.Select(x => x.StoreId)
            .Should().BeEquivalentTo(
                new[]
                {
                    firstSeed.StoreId,
                    secondSeed.StoreId
                });

        heads.Select(x => x.XmlHash)
            .Distinct()
            .Should().ContainSingle();

        (await verificationDb
                .StockDocumentInputInvoiceMaps
                .IgnoreQueryFilters()
                .CountAsync())
            .Should().Be(2);

        (await verificationDb
                .StockDocumentLineInputInvoiceMaps
                .IgnoreQueryFilters()
                .CountAsync())
            .Should().Be(2);
    }

    private static InputInvoiceHead NewCandidate(
        int storeId,
        string xmlHash)
    {
        var candidate = new InputInvoiceHead
        {
            StoreId = storeId,
            SellerTaxCode = "031.277.0607-001",
            InvoiceSeries = " c26 taa ",
            InvoiceNumber = " 000123 ",
            InvoiceDate = new DateTime(
                2026,
                8,
                17,
                12,
                34,
                56),
            XmlHash = xmlHash,
            OriginalFileName = $"{xmlHash}.xml"
        };

        candidate.Details.Add(
            new InputInvoiceDetail
            {
                LineNo = 1,
                ItemName = "Identity item",
                Quantity = 1m,
                UnitPrice = 10m,
                LineAmount = 10m
            });

        InputInvoiceIdentityPolicy.ApplyRequiredIdentity(
            candidate);

        return candidate;
    }

    private static async Task<int> SeedReceiptAsync(
        InventoryPostingLocalDb database,
        InventoryPostingSeed seed)
    {
        await using var db =
            CreateContext(database, seed.StoreId);

        var receipt = new StockDocument
        {
            StoreId = seed.StoreId,
            DocumentNo = $"PN-XML-{Guid.NewGuid():N}",
            Type = StockDocumentType.Receipt,
            Status = StockDocumentStatus.Draft,
            ReceiptSource = PurchaseReceiptSource.Direct,
            DirectReceiptReason =
                "Invoice identity concurrency",
            WarehouseId = seed.WarehouseId,
            DocumentDate = new DateTime(2026, 8, 17)
        };

        receipt.Lines.Add(
            new StockDocumentLine
            {
                LineNo = 1,
                ProductVariantId = seed.ProductVariantId,
                ProductNameSnapshot = "Identity item",
                UnitNameSnapshot = "Unit",
                Quantity = 1m,
                Factor = 1m,
                BaseQuantity = 1m,
                UnitCost = 10m,
                LineTotal = 10m
            });

        db.StockDocuments.Add(receipt);
        await db.SaveChangesAsync();

        return receipt.Id;
    }

    private static byte[] BuildXml(
        string sellerTaxCode = "0312770607",
        string invoiceSeries = "C26TAA",
        string invoiceNumber = "9001",
        string marker = "canonical")
        => Encoding.UTF8.GetBytes(
            $"""
             <HDon><DLHDon>
               <TTChung>
                 <KHMSHDon>1</KHMSHDon>
                 <KHHDon>{invoiceSeries}</KHHDon>
                 <SHDon>{invoiceNumber}</SHDon>
                 <NLap>2026-08-17</NLap>
               </TTChung>
               <NDHDon>
                 <NBan>
                   <MST>{sellerTaxCode}</MST>
                   <Ten>Seller {marker}</Ten>
                 </NBan>
                 <NMua>
                   <MST>0100000001</MST>
                   <Ten>Buyer</Ten>
                 </NMua>
                 <DSHHDVu>
                   <HHDVu>
                     <STT>1</STT>
                     <THHDVu>Identity item {marker}</THHDVu>
                     <DVTinh>Unit</DVTinh>
                     <SLuong>1</SLuong>
                     <DGia>10</DGia>
                     <ThTien>10</ThTien>
                   </HHDVu>
                 </DSHHDVu>
                 <TToan>
                   <TgTCThue>10</TgTCThue>
                   <TgTThue>0</TgTThue>
                   <TgTTTBSo>10</TgTTTBSo>
                 </TToan>
               </NDHDon>
             </DLHDon></HDon>
             """);

    private static UploadInputInvoiceXmlRequest Request(
        int stockDocumentId,
        string fileName,
        byte[] bytes)
        => new()
        {
            StockDocumentId = stockDocumentId,
            OriginalFileName = fileName,
            FileBytes = bytes
        };

    private sealed class
        CompleteLineMapDuringBothSaveAttemptsInterceptor
        : SaveChangesInterceptor
    {
        private readonly InventoryPostingLocalDb _database;
        private readonly int _storeId;
        private readonly int _stockDocumentId;
        private readonly int _stockDocumentLineId;

        private int _saveAttempts;
        private int _failedSaveAttempts;

        public CompleteLineMapDuringBothSaveAttemptsInterceptor(
            InventoryPostingLocalDb database,
            int storeId,
            int stockDocumentId,
            int stockDocumentLineId)
        {
            _database = database;
            _storeId = storeId;
            _stockDocumentId = stockDocumentId;
            _stockDocumentLineId = stockDocumentLineId;
        }

        public int SaveAttempts
            => Volatile.Read(ref _saveAttempts);

        public int FailedSaveAttempts
            => Volatile.Read(ref _failedSaveAttempts);

        public override async ValueTask<InterceptionResult<int>>
            SavingChangesAsync(
                DbContextEventData eventData,
                InterceptionResult<int> result,
                CancellationToken cancellationToken = default)
        {
            var hasTargetAddedMap =
                eventData.Context is not null
                && eventData.Context.ChangeTracker
                    .Entries<StockDocumentLineInputInvoiceMap>()
                    .Any(entry =>
                        entry.State == EntityState.Added
                        && entry.Entity.StoreId == _storeId
                        && entry.Entity.StockDocumentId
                            == _stockDocumentId
                        && entry.Entity.StockDocumentLineId
                            == _stockDocumentLineId);

            if (!hasTargetAddedMap)
                return result;

            var attempt =
                Interlocked.Increment(ref _saveAttempts);

            if (attempt <= 2)
            {
                await InsertCompetingMapAsync(
                    cancellationToken);
            }

            return result;
        }

        public override async Task SaveChangesFailedAsync(
            DbContextErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            var failedAttempt =
                Interlocked.Increment(
                    ref _failedSaveAttempts);

            if (failedAttempt == 1)
            {
                await DeleteCompetingMapAsync(
                    cancellationToken);
            }

            await base.SaveChangesFailedAsync(
                eventData,
                cancellationToken);
        }

        private async Task InsertCompetingMapAsync(
            CancellationToken ct)
        {
            await using var competitorDb =
                _database.CreateTenantContext(_storeId);

            competitorDb.StockDocumentLineInputInvoiceMaps.Add(
                new StockDocumentLineInputInvoiceMap
                {
                    StoreId = _storeId,
                    StockDocumentId = _stockDocumentId,
                    StockDocumentLineId =
                        _stockDocumentLineId,
                    UseInputInvoice = false,
                    InputInvoiceDetailId = null,
                    MatchStatus =
                        InputInvoiceMatchStatus.None,
                    QuantityDifference = 0,
                    AmountDifference = 0
                });

            await competitorDb.SaveChangesAsync(ct);
        }

        private async Task DeleteCompetingMapAsync(
            CancellationToken ct)
        {
            await using var cleanupDb =
                _database.CreateTenantContext(_storeId);

            await cleanupDb
                .StockDocumentLineInputInvoiceMaps
                .Where(x =>
                    x.StoreId == _storeId
                    && x.StockDocumentId
                        == _stockDocumentId
                    && x.StockDocumentLineId
                        == _stockDocumentLineId)
                .ExecuteDeleteAsync(ct);
        }
    }

    private static AppDbContext CreateContext(
        InventoryPostingLocalDb database,
        int storeId)
        => new(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(database.ConnectionString)
                .Options,
            new TenantStub(storeId),
            new UserStub());

    private sealed class TenantStub(int storeId) : ITenantContext
    {
        public int? StoreId => storeId;
        public bool IsHostAdmin => false;
        public string? Subdomain => "input-invoice-identity";
    }

    private sealed class UserStub : ICurrentUser
    {
        public int? UserId => 601;
        public string? UserName => "input-invoice-identity";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}