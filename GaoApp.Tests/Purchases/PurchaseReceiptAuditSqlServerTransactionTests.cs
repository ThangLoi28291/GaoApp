using FluentAssertions;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Services.Purchases;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Tests.Configuration;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Purchases;

[Collection("R1FinalDatabasePreflight")]
public sealed class PurchaseReceiptAuditSqlServerTransactionTests
{
    [Fact]
    public async Task Receipt_mutation_and_event_rollback_or_commit_together()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync();
        var receiptId = await SeedReceiptAsync(database, seed);

        await using (var rollback = CreateContext(database, seed.StoreId))
        {
            var document = await rollback.StockDocuments.SingleAsync(x => x.Id == receiptId);
            var repository = new StockDocumentRepository(rollback);
            await repository.BeginTransactionAsync();
            document.Status = StockDocumentStatus.PendingApproval;
            document.SubmittedAtUtc = DateTime.UtcNow;
            document.SubmittedByUserId = 501;
            PurchaseReceiptAuditEvidence.MarkWorkflowEvent(
                document,
                PurchaseReceiptAuditEventType.SubmittedForApproval);
            await repository.SaveChangesAsync();
            await repository.RollbackTransactionAsync();
        }

        await using (var verifyRollback = CreateContext(database, seed.StoreId))
        {
            (await verifyRollback.StockDocuments
                    .AsNoTracking()
                    .SingleAsync(x => x.Id == receiptId))
                .Status.Should().Be(StockDocumentStatus.Draft);
            (await verifyRollback.PurchaseReceiptAuditEvents.CountAsync())
                .Should().Be(0);
        }

        await using (var commit = CreateContext(database, seed.StoreId))
        {
            var document = await commit.StockDocuments.SingleAsync(x => x.Id == receiptId);
            var repository = new StockDocumentRepository(commit);
            await repository.BeginTransactionAsync();
            document.Status = StockDocumentStatus.PendingApproval;
            document.SubmittedAtUtc = DateTime.UtcNow;
            document.SubmittedByUserId = 501;
            PurchaseReceiptAuditEvidence.MarkWorkflowEvent(
                document,
                PurchaseReceiptAuditEventType.SubmittedForApproval);
            await repository.SaveChangesAsync();
            await repository.CommitTransactionAsync();
        }

        await using (var verifyCommit = CreateContext(database, seed.StoreId))
        {
            (await verifyCommit.StockDocuments
                    .AsNoTracking()
                    .SingleAsync(x => x.Id == receiptId))
                .Status.Should().Be(StockDocumentStatus.PendingApproval);
            var auditEvent = await verifyCommit.PurchaseReceiptAuditEvents
                .AsNoTracking()
                .SingleAsync();
            auditEvent.EventType.Should().Be(
                PurchaseReceiptAuditEventType.SubmittedForApproval);
            auditEvent.ActorUserId.Should().Be(501);
            auditEvent.IsSuccess.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Concurrent_confirmation_rowversion_failure_leaves_one_success_event()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync();
        var receiptId = await SeedReceiptAsync(
            database,
            seed,
            StockDocumentStatus.PendingApproval);

        await using var first = CreateContext(database, seed.StoreId);
        await using var second = CreateContext(database, seed.StoreId);
        var firstDocument = await first.StockDocuments.SingleAsync(x => x.Id == receiptId);
        var secondDocument = await second.StockDocuments.SingleAsync(x => x.Id == receiptId);
        var firstRepository = new StockDocumentRepository(first);
        var secondRepository = new StockDocumentRepository(second);

        await firstRepository.BeginTransactionAsync();
        firstDocument.Status = StockDocumentStatus.Confirmed;
        firstDocument.ConfirmedAtUtc = DateTime.UtcNow;
        firstDocument.ConfirmedByUserId = 501;
        PurchaseReceiptAuditEvidence.MarkWorkflowEvent(
            firstDocument,
            PurchaseReceiptAuditEventType.GenericReceiptConfirmed);
        await firstRepository.SaveChangesAsync();
        await firstRepository.CommitTransactionAsync();

        await secondRepository.BeginTransactionAsync();
        secondDocument.Status = StockDocumentStatus.Confirmed;
        secondDocument.ConfirmedAtUtc = DateTime.UtcNow;
        secondDocument.ConfirmedByUserId = 501;
        PurchaseReceiptAuditEvidence.MarkWorkflowEvent(
            secondDocument,
            PurchaseReceiptAuditEventType.GenericReceiptConfirmed);
        var staleSave = () => secondRepository.SaveChangesAsync();
        await staleSave.Should().ThrowAsync<DbUpdateConcurrencyException>();
        await secondRepository.RollbackTransactionAsync();

        await using var verify = CreateContext(database, seed.StoreId);
        (await verify.PurchaseReceiptAuditEvents
                .CountAsync(x => x.EventType ==
                    PurchaseReceiptAuditEventType.GenericReceiptConfirmed))
            .Should().Be(1);

        var retryRepository = new StockDocumentRepository(verify);
        await retryRepository.SaveChangesAsync();
        (await verify.PurchaseReceiptAuditEvents
                .CountAsync(x => x.EventType ==
                    PurchaseReceiptAuditEventType.GenericReceiptConfirmed))
            .Should().Be(1);
    }

    private static async Task<int> SeedReceiptAsync(
        InventoryPostingLocalDb database,
        InventoryPostingSeed seed,
        StockDocumentStatus status = StockDocumentStatus.Draft)
    {
        await using var db = CreateContext(database, seed.StoreId);
        var document = new StockDocument
        {
            DocumentNo = $"NK-{Guid.NewGuid():N}"[..20],
            Type = StockDocumentType.Receipt,
            Status = status,
            WarehouseId = seed.WarehouseId,
            DocumentDate = DateTime.UtcNow,
            ReceiptSource = PurchaseReceiptSource.Direct,
            DirectReceiptReason = "Mua gấp"
        };
        db.StockDocuments.Add(document);
        await db.SaveChangesAsync();
        return document.Id;
    }

    private static AppDbContext CreateContext(
        InventoryPostingLocalDb database,
        int storeId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(database.ConnectionString)
            .Options;
        return new AppDbContext(
            options,
            new TenantContextStub(storeId),
            new CurrentUserStub());
    }

    private sealed class TenantContextStub(int storeId) : ITenantContext
    {
        public int? StoreId => storeId;
        public bool IsHostAdmin => false;
        public string? Subdomain => "receipt-audit-sql";
    }

    private sealed class CurrentUserStub : ICurrentUser
    {
        public int? UserId => 501;
        public string? UserName => "receipt-audit-sql";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
