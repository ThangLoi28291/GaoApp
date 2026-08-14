using System.Text.Json;
using FluentAssertions;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Services.Purchases;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Purchases;

public sealed class PurchaseReceiptAuditWorkflowTests
{
    [Fact]
    public async Task Line_add_update_delete_persists_allowlisted_before_after_evidence()
    {
        await using var db = CreateContext();
        var document = await SeedReceiptAsync(db);
        var repository = new StockDocumentRepository(db);
        var line = NewLine(document);
        document.Lines.Add(line);

        await repository.SaveChangesAsync();

        var added = await SingleEventAsync(
            db,
            PurchaseReceiptAuditEventType.PhysicalLineAdded);
        ReadFields(added).Should().Contain(
            nameof(StockDocumentLine.ProductVariantId),
            nameof(StockDocumentLine.UnitId),
            nameof(StockDocumentLine.Quantity));
        ReadObject(added.OldValuesJson).EnumerateObject().Should().BeEmpty();

        line.ProductVariantId = 22;
        line.UnitId = 3;
        line.Factor = 6m;
        line.Quantity = 4m;
        line.BaseQuantity = 24m;
        line.UnitPriceBeforeVat = 90m;
        line.TaxId = 5;
        line.TaxRate = 10m;
        line.VatAmount = 36m;
        line.UnitPriceAfterVat = 99m;
        line.UnitCost = 99m;
        line.LineTotal = 396m;
        line.FreightAllocation = 12m;
        await repository.SaveChangesAsync();

        var changed = await SingleEventAsync(
            db,
            PurchaseReceiptAuditEventType.PhysicalLineChanged);
        ReadFields(changed).Should().Contain(
            nameof(StockDocumentLine.ProductVariantId),
            nameof(StockDocumentLine.UnitId),
            nameof(StockDocumentLine.Quantity),
            nameof(StockDocumentLine.UnitPriceBeforeVat),
            nameof(StockDocumentLine.TaxRate),
            nameof(StockDocumentLine.VatAmount),
            nameof(StockDocumentLine.FreightAllocation));
        ReadObject(changed.OldValuesJson)
            .GetProperty(nameof(StockDocumentLine.Quantity))
            .GetDecimal().Should().Be(2m);
        ReadObject(changed.NewValuesJson)
            .GetProperty(nameof(StockDocumentLine.Quantity))
            .GetDecimal().Should().Be(4m);

        var countBeforeUnchangedSave = await db.PurchaseReceiptAuditEvents.CountAsync();
        await repository.SaveChangesAsync();
        (await db.PurchaseReceiptAuditEvents.CountAsync())
            .Should().Be(countBeforeUnchangedSave);

        await repository.RemoveLineAsync(line);
        await repository.SaveChangesAsync();
        var deleted = await SingleEventAsync(
            db,
            PurchaseReceiptAuditEventType.PhysicalLineDeleted);
        ReadObject(deleted.OldValuesJson)
            .GetProperty(nameof(StockDocumentLine.ProductVariantId))
            .GetInt32().Should().Be(22);
        ReadObject(deleted.NewValuesJson).EnumerateObject().Should().BeEmpty();
    }

    [Fact]
    public async Task Workflow_events_capture_semantic_action_reason_and_server_actor()
    {
        await using var db = CreateContext(userId: 77);
        var document = await SeedReceiptAsync(db);
        var repository = new StockDocumentRepository(db);

        document.Status = StockDocumentStatus.PendingApproval;
        document.SubmittedAtUtc = DateTime.UtcNow;
        document.SubmittedByUserId = 77;
        PurchaseReceiptAuditEvidence.MarkWorkflowEvent(
            document,
            PurchaseReceiptAuditEventType.SubmittedForApproval,
            note: "ready");
        await repository.SaveChangesAsync();

        document.HasRevisionRequest = true;
        document.RevisionRequestNote = "wrong quantity";
        document.RevisionRequestedAtUtc = DateTime.UtcNow;
        document.RevisionRequestedByUserId = 77;
        PurchaseReceiptAuditEvidence.MarkWorkflowEvent(
            document,
            PurchaseReceiptAuditEventType.RevisionRequested,
            reason: "wrong quantity");
        await repository.SaveChangesAsync();

        var revision = await SingleEventAsync(
            db,
            PurchaseReceiptAuditEventType.RevisionRequested);
        revision.ActorUserId.Should().Be(77);
        revision.ActorUserName.Should().Be("workflow-audit-test");
        revision.Reason.Should().Be("wrong quantity");
        revision.OccurredAtUtc.Kind.Should().Be(DateTimeKind.Utc);
        revision.IsSuccess.Should().BeTrue();
        ReadFields(revision).Should().Contain(
            nameof(StockDocument.HasRevisionRequest),
            nameof(StockDocument.RevisionRequestNote));
    }

    [Fact]
    public async Task Commercial_confirmation_records_supplier_header_and_financial_line_changes()
    {
        await using var db = CreateContext();
        var document = await SeedReceiptAsync(db);
        var line = NewLine(document);
        document.Lines.Add(line);
        await db.SaveChangesAsync();
        var repository = new StockDocumentRepository(db);

        document.Status = StockDocumentStatus.Confirmed;
        document.SupplierId = 31;
        document.HasVat = true;
        document.HasFreight = true;
        document.FreightTotal = 8m;
        document.ApprovalNote = "approved";
        line.UnitPriceBeforeVat = 100m;
        line.TaxId = 2;
        line.TaxRate = 10m;
        line.VatAmount = 20m;
        line.UnitPriceAfterVat = 110m;
        line.UnitCost = 110m;
        line.LineTotal = 220m;
        line.FreightAllocation = 8m;
        PurchaseReceiptAuditEvidence.MarkWorkflowEvent(
            document,
            PurchaseReceiptAuditEventType.CommercialApprovalConfirmed,
            note: document.ApprovalNote);

        await repository.SaveChangesAsync();

        var confirmation = await SingleEventAsync(
            db,
            PurchaseReceiptAuditEventType.CommercialApprovalConfirmed);
        ReadFields(confirmation).Should().Contain(
            nameof(StockDocument.SupplierId),
            nameof(StockDocument.HasVat),
            nameof(StockDocument.FreightTotal),
            nameof(StockDocument.Status),
            nameof(StockDocument.ApprovalNote));
        var lineChange = await SingleEventAsync(
            db,
            PurchaseReceiptAuditEventType.PhysicalLineChanged);
        ReadFields(lineChange).Should().Contain(
            nameof(StockDocumentLine.UnitPriceBeforeVat),
            nameof(StockDocumentLine.VatAmount),
            nameof(StockDocumentLine.UnitCost),
            nameof(StockDocumentLine.FreightAllocation));
    }

    private static async Task<PurchaseReceiptAuditEvent> SingleEventAsync(
        InMemoryAppDbContext db,
        PurchaseReceiptAuditEventType eventType)
        => await db.PurchaseReceiptAuditEvents
            .AsNoTracking()
            .SingleAsync(x => x.EventType == eventType);

    private static string[] ReadFields(PurchaseReceiptAuditEvent auditEvent)
        => JsonSerializer.Deserialize<string[]>(auditEvent.ChangedFieldsJson)!;

    private static JsonElement ReadObject(string json)
        => JsonSerializer.Deserialize<JsonElement>(json);

    private static async Task<StockDocument> SeedReceiptAsync(
        InMemoryAppDbContext db)
    {
        var document = new StockDocument
        {
            DocumentNo = $"NK-{Guid.NewGuid():N}"[..20],
            Type = StockDocumentType.Receipt,
            Status = StockDocumentStatus.Draft,
            WarehouseId = 10,
            DocumentDate = DateTime.UtcNow,
            ReceiptSource = PurchaseReceiptSource.Direct,
            DirectReceiptReason = "Mua gấp"
        };
        db.StockDocuments.Add(document);
        await db.SaveChangesAsync();
        return document;
    }

    private static StockDocumentLine NewLine(StockDocument document)
        => new()
        {
            StockDocument = document,
            StockDocumentId = document.Id,
            LineNo = 1,
            ProductVariantId = 21,
            UnitId = 2,
            Factor = 5m,
            Quantity = 2m,
            BaseQuantity = 10m,
            UnitPriceBeforeVat = 80m,
            UnitCost = 80m,
            UnitPriceAfterVat = 80m,
            LineTotal = 160m,
            ProductNameSnapshot = "Audit item"
        };

    private static InMemoryAppDbContext CreateContext(
        int storeId = 1,
        int userId = 10)
    {
        var options = new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase($"receipt-workflow-audit-{Guid.NewGuid():N}")
            .Options;
        return new InMemoryAppDbContext(
            options,
            new TenantContextStub(storeId),
            new CurrentUserStub(userId));
    }

    private sealed class TenantContextStub(int storeId) : ITenantContext
    {
        public int? StoreId => storeId;
        public bool IsHostAdmin => false;
        public string? Subdomain => "workflow-audit-test";
    }

    private sealed class CurrentUserStub(int userId) : ICurrentUser
    {
        public int? UserId => userId;
        public string? UserName => "workflow-audit-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
