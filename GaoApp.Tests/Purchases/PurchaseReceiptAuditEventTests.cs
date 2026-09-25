using System.Text.Json;
using FluentAssertions;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Services.Purchases;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Purchases;

// R2.4-C2 coverage: owner decisions use bounded, server-authored receipt audit evidence.
public sealed class PurchaseReceiptAuditEventTests
{
    [Fact]
    public void Split_lineage_event_types_are_schema_neutral_enum_additions()
    {
        Enum.IsDefined(PurchaseReceiptAuditEventType.ReceiptSplitSource).Should().BeTrue();
        Enum.IsDefined(PurchaseReceiptAuditEventType.ReceiptSplitChild).Should().BeTrue();
    }

    [Fact]
    public void Input_invoice_relationship_events_are_schema_neutral_enum_values()
    {
        ((int)PurchaseReceiptAuditEventType.InputInvoiceLinked).Should().Be(13);
        ((int)PurchaseReceiptAuditEventType.InputInvoiceUnlinked).Should().Be(14);
        Enum.TryParse<PurchaseReceiptAuditEventType>("InputInvoiceRelinked", out var relinked)
            .Should().BeTrue();
        ((int)relinked).Should().Be(28);
    }

    [Fact]
    public void Input_invoice_item_mapping_events_are_schema_neutral_enum_values()
    {
        ((int)PurchaseReceiptAuditEventType.InputInvoiceItemMappingConfirmed)
            .Should().Be(19);
        ((int)PurchaseReceiptAuditEventType.InputInvoiceItemMappingAutoApplied)
            .Should().Be(20);
    }

    [Fact]
    public void Reconciliation_events_are_bounded_schema_neutral_enum_values()
    {
        ((int)PurchaseReceiptAuditEventType.InputInvoiceReconciliationStateChanged)
            .Should().Be(27);
        ((int)PurchaseReceiptAuditEventType.InputInvoiceReconciliationAccepted)
            .Should().Be(21);
        ((int)PurchaseReceiptAuditEventType.InputInvoiceReconciliationAcceptanceInvalidated)
            .Should().Be(22);
        ((int)PurchaseReceiptAuditEventType.InputInvoiceXmlDetailIgnored)
            .Should().Be(23);
        ((int)PurchaseReceiptAuditEventType.InputInvoiceXmlDetailUnignored)
            .Should().Be(24);
        ((int)PurchaseReceiptAuditEventType.InputInvoiceReceiptLineExcluded)
            .Should().Be(25);
        ((int)PurchaseReceiptAuditEventType.InputInvoiceReceiptLineIncluded)
            .Should().Be(26);
    }

    [Fact]
    public void Canonical_allowlists_cover_sensitive_fields_without_unsafe_data()
    {
        PurchaseReceiptAuditEvidence.HeaderFields.Should().Contain(
            nameof(StockDocument.SupplierId),
            nameof(StockDocument.WarehouseId),
            nameof(StockDocument.Status),
            nameof(StockDocument.ApprovalNote),
            nameof(StockDocument.RevisionRequestNote));
        PurchaseReceiptAuditEvidence.LineFields.Should().Contain(
            nameof(StockDocumentLine.ProductVariantId),
            nameof(StockDocumentLine.UnitId),
            nameof(StockDocumentLine.Quantity),
            nameof(StockDocumentLine.UnitPriceBeforeVat),
            nameof(StockDocumentLine.VatAmount),
            nameof(StockDocumentLine.FreightAllocation));

        PurchaseReceiptAuditEvidence.HeaderFields.Should().NotContain(
            "RowVersion",
            "Supplier",
            "Lines",
            "ConnectionString",
            "Token");
        PurchaseReceiptAuditEvidence.LineFields.Should().NotContain(
            "RowVersion",
            "ProductVariant",
            "StockDocument",
            "InputInvoiceMaps");
    }

    [Fact]
    public void Json_evidence_orders_fields_deterministically()
    {
        PurchaseReceiptAuditEvidence.SerializeChangedFields(
                new[] { "Quantity", "Factor", "BaseQuantity", "Factor" })
            .Should().Be("[\"BaseQuantity\",\"Factor\",\"Quantity\"]");

        using var values = JsonDocument.Parse(
            PurchaseReceiptAuditEvidence.SerializeValues(
                new Dictionary<string, object?>
                {
                    ["Quantity"] = 2m,
                    ["Factor"] = 3m
                }));
        values.RootElement.EnumerateObject()
            .Select(x => x.Name)
            .Should().Equal("Factor", "Quantity");
    }

    [Fact]
    public void Json_evidence_restores_utc_kind_without_shifting_database_clock_value()
    {
        var databaseUtcValue = new DateTime(
            2026,
            8,
            14,
            10,
            0,
            0,
            DateTimeKind.Unspecified);

        using var values = JsonDocument.Parse(
            PurchaseReceiptAuditEvidence.SerializeValues(
                new Dictionary<string, object?>
                {
                    [nameof(StockDocument.SubmittedAtUtc)] = databaseUtcValue
                }));

        values.RootElement
            .GetProperty(nameof(StockDocument.SubmittedAtUtc))
            .GetString()
            .Should().Be("2026-08-14T10:00:00Z");
    }

    [Fact]
    public void Json_evidence_preserves_document_date_as_timezone_free_calendar_value()
    {
        var documentDate = new DateTime(
            2026,
            8,
            14,
            10,
            0,
            0,
            DateTimeKind.Local);

        using var values = JsonDocument.Parse(
            PurchaseReceiptAuditEvidence.SerializeValues(
                new Dictionary<string, object?>
                {
                    [nameof(StockDocument.DocumentDate)] = documentDate
                }));

        values.RootElement
            .GetProperty(nameof(StockDocument.DocumentDate))
            .GetString()
            .Should().Be("2026-08-14T10:00:00");
    }

    [Fact]
    public void Model_requires_canonical_authority_and_evidence_fields()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(PurchaseReceiptAuditEvent));
        entity.Should().NotBeNull();
        foreach (var propertyName in new[]
                 {
                     nameof(PurchaseReceiptAuditEvent.StoreId),
                     nameof(PurchaseReceiptAuditEvent.StockDocumentId),
                     nameof(PurchaseReceiptAuditEvent.EventType),
                     nameof(PurchaseReceiptAuditEvent.ActorUserId),
                     nameof(PurchaseReceiptAuditEvent.OccurredAtUtc),
                     nameof(PurchaseReceiptAuditEvent.ChangedFieldsJson),
                     nameof(PurchaseReceiptAuditEvent.OldValuesJson),
                     nameof(PurchaseReceiptAuditEvent.NewValuesJson),
                     nameof(PurchaseReceiptAuditEvent.IsSuccess)
                 })
        {
            entity!.FindProperty(propertyName)!.IsNullable.Should().BeFalse();
        }
    }

    [Fact]
    public async Task Added_event_uses_server_store_actor_and_utc_time()
    {
        await using var db = CreateContext(storeId: 7, userId: 42);
        var forgedLocalTime = new DateTime(2001, 2, 3, 4, 5, 6,
            DateTimeKind.Local);
        var auditEvent = NewEvent();
        auditEvent.StoreId = 999;
        auditEvent.ActorUserId = 998;
        auditEvent.ActorUserName = "forged";
        auditEvent.OccurredAtUtc = forgedLocalTime;
        auditEvent.IsSuccess = false;

        db.PurchaseReceiptAuditEvents.Add(auditEvent);
        await db.SaveChangesAsync();

        auditEvent.StoreId.Should().Be(7);
        auditEvent.ActorUserId.Should().Be(42);
        auditEvent.ActorUserName.Should().Be("audit-test");
        auditEvent.OccurredAtUtc.Kind.Should().Be(DateTimeKind.Utc);
        auditEvent.OccurredAtUtc.Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));
        auditEvent.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Existing_event_cannot_be_updated_or_deleted()
    {
        var databaseName = $"receipt-audit-{Guid.NewGuid():N}";
        await using (var seed = CreateContext(databaseName: databaseName))
        {
            seed.PurchaseReceiptAuditEvents.Add(NewEvent());
            await seed.SaveChangesAsync();
        }

        await using (var update = CreateContext(databaseName: databaseName))
        {
            var auditEvent = await update.PurchaseReceiptAuditEvents.SingleAsync();
            auditEvent.Note = "changed";
            var action = () => update.SaveChangesAsync();
            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*append-only*");
        }

        await using (var delete = CreateContext(databaseName: databaseName))
        {
            var auditEvent = await delete.PurchaseReceiptAuditEvents.SingleAsync();
            delete.PurchaseReceiptAuditEvents.Remove(auditEvent);
            var action = () => delete.SaveChangesAsync();
            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*append-only*");
        }
    }

    private static PurchaseReceiptAuditEvent NewEvent()
        => new()
        {
            StockDocumentId = 123,
            EventType = PurchaseReceiptAuditEventType.ReceiptCreated,
            ChangedFieldsJson = "[]",
            OldValuesJson = "{}",
            NewValuesJson = "{}"
        };

    private static InMemoryAppDbContext CreateContext(
        int storeId = 1,
        int userId = 10,
        string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase(databaseName ?? $"receipt-audit-{Guid.NewGuid():N}")
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
        public string? Subdomain => "audit-test";
    }

    private sealed class CurrentUserStub(int userId) : ICurrentUser
    {
        public int? UserId => userId;
        public string? UserName => "audit-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
