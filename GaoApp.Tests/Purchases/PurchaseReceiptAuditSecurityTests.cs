using System.Reflection;
using FluentAssertions;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Application.Services.Purchases;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Tests.Data;
using GaoApp.Web.Areas.Admin.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GaoApp.Tests.Purchases;

public sealed class PurchaseReceiptAuditSecurityTests
{
    [Fact]
    public async Task Timeline_query_requires_receipt_event_and_current_store_to_match()
    {
        var databaseName = $"receipt-audit-security-{Guid.NewGuid():N}";
        var root = new InMemoryDatabaseRoot();
        int receiptId;

        await using (var storeA = CreateContext(databaseName, root, 1))
        {
            var receipt = NewReceipt();
            storeA.StockDocuments.Add(receipt);
            await storeA.SaveChangesAsync();
            receiptId = receipt.Id;
            receipt.Status = StockDocumentStatus.PendingApproval;
            PurchaseReceiptAuditEvidence.MarkWorkflowEvent(
                receipt,
                PurchaseReceiptAuditEventType.SubmittedForApproval);
            await new StockDocumentRepository(storeA).SaveChangesAsync();

            var timeline = await new StockDocumentRepository(storeA)
                .GetPurchaseReceiptAuditEventsAsync(receiptId);
            timeline.Should().ContainSingle();
            timeline![0].StoreId.Should().Be(1);
        }

        await using (var storeB = CreateContext(databaseName, root, 2))
        {
            var repository = new StockDocumentRepository(storeB);
            (await repository.GetPurchaseReceiptAuditEventsAsync(receiptId))
                .Should().BeNull();
            (await repository.GetPurchaseReceiptAuditEventsAsync(987654))
                .Should().BeNull();
            (await storeB.PurchaseReceiptAuditEvents.CountAsync())
                .Should().Be(0);
        }
    }

    [Fact]
    public void Timeline_endpoint_is_read_only_and_requires_sensitive_audit_permission()
    {
        var method = typeof(StockDocumentsController).GetMethod(
            nameof(StockDocumentsController.GetAuditTimeline));
        method.Should().NotBeNull();
        method!.GetCustomAttribute<HttpGetAttribute>()!.Template
            .Should().Be("{id:int}/audit-events");
        method.GetCustomAttributes<AuthorizeAttribute>()
            .Should().ContainSingle(x =>
                x.Policy == PermissionCodes.System.AuditLog.View);

        typeof(StockDocumentsController).GetMethods(BindingFlags.Public |
                BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(x => x.GetCustomAttributes<HttpMethodAttribute>()
                .Any(attribute =>
                    attribute.Template?.Contains(
                        "audit-events",
                        StringComparison.OrdinalIgnoreCase) == true))
            .Should().ContainSingle()
            .Which.Name.Should().Be(
                nameof(StockDocumentsController.GetAuditTimeline));
    }

    [Fact]
    public void No_receipt_audit_input_contract_accepts_authority_fields()
    {
        var inputTypes = typeof(GaoApp.Application.DTOs.Inventory.PurchaseReceiptAuditEventDto)
            .Assembly.GetTypes()
            .Where(type => type.Namespace ==
                    "GaoApp.Application.DTOs.Inventory" &&
                type.Name.Contains("PurchaseReceiptAudit", StringComparison.Ordinal) &&
                type.Name.Contains("Request", StringComparison.Ordinal))
            .ToArray();
        inputTypes.Should().BeEmpty();
    }

    private static StockDocument NewReceipt()
        => new()
        {
            DocumentNo = $"NK-{Guid.NewGuid():N}"[..20],
            Type = StockDocumentType.Receipt,
            Status = StockDocumentStatus.Draft,
            WarehouseId = 1,
            DocumentDate = DateTime.UtcNow,
            ReceiptSource = PurchaseReceiptSource.Direct,
            DirectReceiptReason = "Mua gấp"
        };

    private static InMemoryAppDbContext CreateContext(
        string databaseName,
        InMemoryDatabaseRoot root,
        int storeId)
    {
        var options = new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase(databaseName, root)
            .Options;
        return new InMemoryAppDbContext(
            options,
            new TenantContextStub(storeId),
            new CurrentUserStub());
    }

    private sealed class TenantContextStub(int storeId) : ITenantContext
    {
        public int? StoreId => storeId;
        public bool IsHostAdmin => false;
        public string? Subdomain => $"store-{storeId}";
    }

    private sealed class CurrentUserStub : ICurrentUser
    {
        public int? UserId => 10;
        public string? UserName => "security-audit-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
