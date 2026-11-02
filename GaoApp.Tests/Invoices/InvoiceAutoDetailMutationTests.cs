using System.Globalization;
using System.Text.Json;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Application.Services.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Invoices;
using GaoApp.Infrastructure.Repositories.LegalEntities;
using GaoApp.Infrastructure.Repositories.Orders;
using GaoApp.Infrastructure.Repositories.Products;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Invoices;

public sealed class InvoiceAutoDetailMutationTests
{
    private static readonly DateTime OriginalChangeAt = new(2026, 10, 1, 1, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Auto_update_recalculates_invoice_and_preserves_POS_product_and_source_identity(bool allocated)
    {
        await using var db = CreateContext(out _);
        await SeedAsync(db, allocated);
        db.ChangeTracker.Clear();
        var service = Service(db);
        var result = await service.UpdateDetailAsync(new UpdateManualInvoiceDetailRequest
        {
            InvoiceDetailId = 81, Quantity = 1.235m, UnitPrice = 19.99m, VatRate = 8m,
            Note = "  Ghi chú trên hóa đơn  "
        });
        Assert.True(result.IsSuccess, result.Error.Message);
        Assert.Equal(InvoiceIssuanceRoute.Automatic, result.Value.InvoiceIssuanceRoute);
        Assert.True(result.Value.CanEditDetails);
        db.ChangeTracker.Clear();
        var detail = await db.InvoiceDetails.SingleAsync(x => x.Id == 81);
        Assert.Equal(1.235m, detail.Quantity);
        Assert.Equal(19.99m, detail.UnitPrice);
        Assert.Equal(24.69m, detail.Amount);
        Assert.Equal(1.98m, detail.VatAmount);
        Assert.Equal(26.67m, detail.TotalAmount);
        Assert.Equal("Ghi chú trên hóa đơn", detail.Note);
        Assert.Equal(InvoiceDetailSourceType.FromOrderLine, detail.SourceType);
        Assert.Equal(51, detail.OrderLineId);
        Assert.Equal(allocated ? 61 : (int?)null, detail.OrderLegalEntityAllocationId);
        Assert.Equal(31, detail.ProductVariantId);
        Assert.Equal("Sản phẩm nguồn", detail.ItemName);
        Assert.Equal("Cái", detail.UnitName);
        var head = await db.InvoiceHeads.SingleAsync(x => x.Id == 71);
        Assert.Equal(2.235m, head.TotalQuantity);
        Assert.Equal(74.69m, head.SubTotal);
        Assert.Equal(1.98m, head.VatAmount);
        Assert.Equal(76.67m, head.GrandTotal);
        Assert.True(head.LastIssuanceRelevantChangeAtUtc > OriginalChangeAt);
        await AssertOriginalSaleUnchangedAsync(db, allocated);
        Assert.Equal(0, await db.InventoryTransactions.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Auto_delete_is_soft_and_excludes_only_the_invoice_line_from_totals(bool allocated)
    {
        await using var db = CreateContext(out _);
        await SeedAsync(db, allocated);
        db.ChangeTracker.Clear();
        var result = await Service(db).DeleteDetailAsync(81);
        Assert.True(result.IsSuccess, result.Error.Message);
        Assert.DoesNotContain(result.Value.Details, x => x.Id == 81);
        db.ChangeTracker.Clear();
        Assert.False(await db.InvoiceDetails.AnyAsync(x => x.Id == 81));
        var removed = await db.InvoiceDetails.IgnoreQueryFilters().SingleAsync(x => x.Id == 81);
        Assert.True(removed.IsDeleted);
        Assert.NotNull(removed.DeletedAtUtc);
        Assert.Equal(InvoiceDetailSourceType.FromOrderLine, removed.SourceType);
        Assert.Equal(51, removed.OrderLineId);
        Assert.Equal(allocated ? 61 : (int?)null, removed.OrderLegalEntityAllocationId);
        Assert.Equal(31, removed.ProductVariantId);
        var head = await db.InvoiceHeads.SingleAsync(x => x.Id == 71);
        Assert.Equal(1m, head.TotalQuantity);
        Assert.Equal(50m, head.SubTotal);
        Assert.Equal(0m, head.VatAmount);
        Assert.Equal(50m, head.GrandTotal);
        Assert.True(head.LastIssuanceRelevantChangeAtUtc > OriginalChangeAt);
        await AssertOriginalSaleUnchangedAsync(db, allocated);
    }

    [Fact]
    public async Task Adding_a_product_to_an_AUTO_invoice_creates_Manual_provenance_and_keeps_the_original_auto_line()
    {
        await using var db = CreateContext(out _);
        await SeedAsync(db, allocated: true);
        db.Warehouses.Add(new Warehouse { Id=1,StoreId=1,LegalEntityId=21,Code="AUTO-ADD",Name="Kho thêm hàng",IsActive=true,IsDefault=true });
        (await db.LegalEntities.SingleAsync(x=>x.Id==21)).DefaultWarehouseId=1;
        db.POSTerminals.Add(new POSTerminal { Id=1,StoreId=1,Code="AUTO-ADD",Name="Quầy kiểm thử" });
        db.POSShifts.Add(new POSShift { Id=1,StoreId=1,TerminalId=1,WarehouseId=1,OpenedByUserId=99 });
        db.InvoiceInputStockSupplementalMovements.Add(new InvoiceInputStockSupplementalMovement { StoreId=1,WarehouseId=1,ProductVariantId=31,
            QuantityChange=100,MovementType=InvoiceInputStockSupplementalMovementType.LegacyOpening,EffectiveAtUtc=DateTime.UtcNow,LegacySourceKey="AUTO-ADD-OPENING" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var result = await Service(db).AddManualDetailAsync(new CreateManualInvoiceDetailRequest
        {
            InvoiceHeadId = 71, ProductVariantId = 31, ItemName = "Sản phẩm bổ sung", UnitName = "Cái",
            Quantity = 2m, UnitPrice = 25m, VatRate = 10m
        });
        Assert.True(result.IsSuccess, result.Error.Message);
        db.ChangeTracker.Clear();
        var added = await db.InvoiceDetails.SingleAsync(x => x.ItemName == "Sản phẩm bổ sung");
        Assert.Equal(InvoiceDetailSourceType.Manual, added.SourceType);
        Assert.Null(added.OrderLineId);
        Assert.Null(added.OrderLegalEntityAllocationId);
        Assert.Equal(31, added.ProductVariantId);
        Assert.Equal(55m, added.TotalAmount);
        var auto = await db.InvoiceDetails.SingleAsync(x => x.Id == 81);
        Assert.Equal(InvoiceDetailSourceType.FromOrderLine, auto.SourceType);
        Assert.Equal(2m, auto.Quantity);
        Assert.Equal(305m, (await db.InvoiceHeads.SingleAsync(x => x.Id == 71)).GrandTotal);
        await AssertOriginalSaleUnchangedAsync(db, allocated: true);
    }

    [Fact]
    public async Task Manual_only_endpoints_still_reject_AUTO_lines_without_changing_any_amount()
    {
        await using var db = CreateContext(out _);
        await SeedAsync(db);
        var before = await SnapshotAsync(db);
        var service = Service(db);
        var update = await service.UpdateManualDetailAsync(Change());
        var delete = await service.DeleteManualDetailAsync(81);
        Assert.False(update.IsSuccess);
        Assert.Equal("Invoice.CannotEditAutoLine", update.Error.Code);
        Assert.False(delete.IsSuccess);
        Assert.Equal("Invoice.CannotDeleteAutoLine", delete.Error.Code);
        Assert.Equal(before, await SnapshotAsync(db));
    }

    [Theory]
    [InlineData(InvoiceProviderStatus.LocalDraft)]
    [InlineData(InvoiceProviderStatus.ReadyToIssue)]
    [InlineData(InvoiceProviderStatus.Previewed)]
    [InlineData(InvoiceProviderStatus.IssueFailed)]
    public async Task Definite_pre_issue_states_allow_AUTO_edits(InvoiceProviderStatus status)
    {
        await using var db = CreateContext(out _);
        await SeedAsync(db);
        var head = await db.InvoiceHeads.SingleAsync(x => x.Id == 71);
        head.ProviderStatus = status;
        head.LastErrorCode = status == InvoiceProviderStatus.IssueFailed ? "HTTP_400" : null;
        await db.SaveChangesAsync();
        var result = await Service(db).UpdateDetailAsync(Change());
        Assert.True(result.IsSuccess, result.Error.Message);
    }

    [Theory]
    [InlineData(InvoiceProviderStatus.DraftSent)]
    [InlineData(InvoiceProviderStatus.Issuing)]
    [InlineData(InvoiceProviderStatus.IssuedWaitingNumber)]
    [InlineData(InvoiceProviderStatus.Issued)]
    [InlineData(InvoiceProviderStatus.PdfDownloaded)]
    [InlineData(InvoiceProviderStatus.ZipDownloaded)]
    [InlineData(InvoiceProviderStatus.EmailSent)]
    [InlineData(InvoiceProviderStatus.Cancelled)]
    public async Task Provider_owned_states_reject_update_delete_and_add_without_side_effects(InvoiceProviderStatus status)
    {
        await using var db = CreateContext(out _);
        await SeedAsync(db);
        (await db.InvoiceHeads.SingleAsync(x => x.Id == 71)).ProviderStatus = status;
        await db.SaveChangesAsync();
        await AssertMutationsRejectedAsync(db);
    }

    [Theory]
    [InlineData("locked")]
    [InlineData("legacy-readonly")]
    [InlineData("provider-number")]
    [InlineData("issued-at")]
    public async Task Immutable_head_evidence_blocks_mutation_even_when_provider_status_says_draft(string reason)
    {
        await using var db = CreateContext(out _);
        await SeedAsync(db);
        var head = await db.InvoiceHeads.SingleAsync(x => x.Id == 71);
        switch (reason)
        {
            case "locked": head.IsLocked = true; break;
            case "legacy-readonly": head.LegacyReadOnly = true; break;
            case "provider-number": head.ProviderInvoiceNo = "TEST-ISSUED-71"; break;
            case "issued-at": head.IssuedAtUtc = OriginalChangeAt; break;
        }
        await db.SaveChangesAsync();
        await AssertMutationsRejectedAsync(db);
    }

    [Theory]
    [InlineData("TIMEOUT")]
    [InlineData("UNKNOWN_RESULT")]
    [InlineData("HTTP_500")]
    [InlineData("HTTP_503")]
    public async Task Uncertain_provider_evidence_blocks_draft_mutation_using_raw_head_state(string code)
    {
        await using var db = CreateContext(out _);
        await SeedAsync(db);
        (await db.InvoiceHeads.SingleAsync(x => x.Id == 71)).LastErrorCode = code;
        await db.SaveChangesAsync();
        await AssertMutationsRejectedAsync(db);
    }

    [Fact]
    public async Task Sanitizing_private_provider_diagnostics_does_not_make_the_detail_DTO_editable()
    {
        await using var db = CreateContext(out _);
        await SeedAsync(db);
        var head = await db.InvoiceHeads.SingleAsync(x => x.Id == 71);
        head.LastErrorCode = "INTERNAL_PROVIDER_FAILURE";
        head.LastErrorMessage = "Internal provider timeout; credential=synthetic-private-diagnostic";
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var read = await new InvoiceReadService(new InvoiceRepository(db), new ProductVariantRepository(db)).GetInvoiceDetailAsync(71);
        Assert.True(read.IsSuccess, read.Error.Message);
        Assert.False(read.Value.CanEditDetails);
        Assert.NotEqual(head.LastErrorMessage, read.Value.LastErrorMessage);
        Assert.False(read.Value.LastErrorMessage!.Contains("timeout", StringComparison.OrdinalIgnoreCase));
        Assert.False(read.Value.LastErrorMessage.Contains("synthetic-private-diagnostic", StringComparison.Ordinal));
        await AssertMutationsRejectedAsync(db);
    }

    [Theory]
    [InlineData(AutoInvoiceOperationStatus.Pending, false)]
    [InlineData(AutoInvoiceOperationStatus.Processing, false)]
    [InlineData(AutoInvoiceOperationStatus.Unknown, false)]
    [InlineData(AutoInvoiceOperationStatus.Succeeded, false)]
    [InlineData(AutoInvoiceOperationStatus.Pending, true)]
    [InlineData(AutoInvoiceOperationStatus.Processing, true)]
    [InlineData(AutoInvoiceOperationStatus.Unknown, true)]
    [InlineData(AutoInvoiceOperationStatus.Succeeded, true)]
    [InlineData(AutoInvoiceOperationStatus.Failed, true)]
    [InlineData(AutoInvoiceOperationStatus.Blocked, true)]
    public async Task Active_issuance_claim_blocks_mutation_of_direct_or_group_source_invoice(AutoInvoiceOperationStatus status, bool grouped)
    {
        await using var db = CreateContext(out _);
        await SeedAsync(db);
        var operation = new AutoInvoiceOperation
        {
            Id = 91, StoreId = 1, Status = status,
            Kind = grouped ? AutoInvoiceOperationKind.Group : AutoInvoiceOperationKind.Single,
            InvoiceHeadId = grouped ? null : 71,
            Sources = grouped ? [new AutoInvoiceOperationSource
            {
                Id = 92, StoreId = 1, InvoiceHeadId = 71, OrderId = 41, InvoiceDetailId = 81,
                OrderLineId = 51, IsActive = true,
                Status = status == AutoInvoiceOperationStatus.Succeeded ? AutoInvoiceSourceStatus.Succeeded : AutoInvoiceSourceStatus.Claimed
            }] : []
        };
        db.AutoInvoiceOperations.Add(operation);
        await db.SaveChangesAsync();
        await AssertMutationsRejectedAsync(db);
    }

    [Theory]
    [InlineData("0", "100", "0")]
    [InlineData("-1", "100", "0")]
    [InlineData("1", "-1", "0")]
    [InlineData("1", "100", "-1")]
    [InlineData("1", "100", "100.01")]
    public async Task Invalid_AUTO_amounts_are_rejected_without_altering_persisted_data(string quantity, string unitPrice, string vatRate)
    {
        await using var db = CreateContext(out _);
        await SeedAsync(db);
        var before = await SnapshotAsync(db);
        var result = await Service(db).UpdateDetailAsync(new UpdateManualInvoiceDetailRequest
        {
            InvoiceDetailId = 81, Quantity = decimal.Parse(quantity, CultureInfo.InvariantCulture),
            UnitPrice = decimal.Parse(unitPrice, CultureInfo.InvariantCulture), VatRate = decimal.Parse(vatRate, CultureInfo.InvariantCulture)
        });
        Assert.False(result.IsSuccess);
        db.ChangeTracker.Clear();
        Assert.Equal(before, await SnapshotAsync(db));
    }

    [Fact]
    public async Task Foreign_tenant_cannot_update_or_delete_the_source_line()
    {
        await using var db = CreateContext(out var tenant);
        await SeedAsync(db);
        var before = await SnapshotAsync(db);
        db.ChangeTracker.Clear(); tenant.SetStore(2, "store-two");
        Assert.False((await Service(db).UpdateDetailAsync(Change())).IsSuccess);
        Assert.False((await Service(db).DeleteDetailAsync(81)).IsSuccess);
        db.ChangeTracker.Clear(); tenant.SetStore(1, "store-one");
        Assert.Equal(before, await SnapshotAsync(db));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deleted_invoice_or_detail_cannot_be_mutated(bool deletedHead)
    {
        await using var db = CreateContext(out _);
        await SeedAsync(db);
        if (deletedHead) (await db.InvoiceHeads.SingleAsync(x => x.Id == 71)).IsDeleted = true;
        else (await db.InvoiceDetails.SingleAsync(x => x.Id == 81)).IsDeleted = true;
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var before = await SnapshotAsync(db);
        Assert.False((await Service(db).UpdateDetailAsync(Change())).IsSuccess);
        Assert.False((await Service(db).DeleteDetailAsync(81)).IsSuccess);
        Assert.Equal(before, await SnapshotAsync(db));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Regeneration_does_not_recreate_an_intentionally_deleted_AUTO_source_after_reloading_context(bool allocated)
    {
        await using var db = CreateContext(out _);
        await SeedAsync(db, allocated, includeInvoice: false);
        var service = Service(db);
        var generated = await service.GenerateDetailsFromOrderLinesAsync(41);
        Assert.True(generated.IsSuccess, generated.Error.Message);
        var detail = Assert.Single(generated.Value.Details);
        Assert.True((await service.DeleteDetailAsync(detail.Id)).IsSuccess);
        db.ChangeTracker.Clear();
        var regenerated = await service.GenerateDetailsFromOrderLinesAsync(41);
        Assert.True(regenerated.IsSuccess, regenerated.Error.Message);
        Assert.Empty(regenerated.Value.Details);
        Assert.Equal(0m, regenerated.Value.GrandTotal);
        var stored = await db.InvoiceDetails.IgnoreQueryFilters().Where(x => x.InvoiceHeadId == generated.Value.Id).ToListAsync();
        var tombstone = Assert.Single(stored);
        Assert.Equal(detail.Id, tombstone.Id); Assert.True(tombstone.IsDeleted);
        Assert.Equal(51, tombstone.OrderLineId);
        Assert.Equal(allocated ? 61 : (int?)null, tombstone.OrderLegalEntityAllocationId);
        await AssertOriginalSaleUnchangedAsync(db, allocated);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Regeneration_preserves_edited_AUTO_amounts_instead_of_copying_order_amounts_back(bool allocated)
    {
        await using var db = CreateContext(out _);
        await SeedAsync(db, allocated, includeInvoice: false);
        var service = Service(db);
        var generated = await service.GenerateDetailsFromOrderLinesAsync(41);
        Assert.True(generated.IsSuccess, generated.Error.Message);
        var id = Assert.Single(generated.Value.Details).Id;
        var changed = await service.UpdateDetailAsync(new UpdateManualInvoiceDetailRequest
        {
            InvoiceDetailId = id, Quantity = 0.5m, UnitPrice = 333m, VatRate = 8m, Note = "Hóa đơn đã chỉnh"
        });
        Assert.True(changed.IsSuccess, changed.Error.Message);
        db.ChangeTracker.Clear();
        var regenerated = await service.GenerateDetailsFromOrderLinesAsync(41);
        Assert.True(regenerated.IsSuccess, regenerated.Error.Message);
        var detail = Assert.Single(regenerated.Value.Details);
        Assert.Equal(id, detail.Id); Assert.Equal(0.5m, detail.Quantity); Assert.Equal(333m, detail.UnitPrice);
        Assert.Equal(8m, detail.VatRate); Assert.Equal(179.82m, detail.TotalAmount);
        await AssertOriginalSaleUnchangedAsync(db, allocated);
    }

    private static UpdateManualInvoiceDetailRequest Change() => new()
    {
        InvoiceDetailId = 81, Quantity = 1m, UnitPrice = 150m, VatRate = 8m, Note = "Thử sửa hóa đơn"
    };

    private static async Task AssertMutationsRejectedAsync(InMemoryAppDbContext db)
    {
        db.ChangeTracker.Clear();
        var before = await SnapshotAsync(db);
        var service = Service(db);
        Assert.False((await service.UpdateDetailAsync(Change())).IsSuccess);
        Assert.False((await service.DeleteDetailAsync(81)).IsSuccess);
        Assert.False((await service.UpdateManualDetailAsync(new UpdateManualInvoiceDetailRequest
        {
            InvoiceDetailId = 82, Quantity = 2m, UnitPrice = 75m, VatRate = 8m
        })).IsSuccess);
        Assert.False((await service.DeleteManualDetailAsync(82)).IsSuccess);
        Assert.False((await service.AddManualDetailAsync(new CreateManualInvoiceDetailRequest
        {
            InvoiceHeadId = 71, ItemName = "Không được thêm", Quantity = 1m, UnitPrice = 25m, VatRate = 0m
        })).IsSuccess);
        db.ChangeTracker.Clear();
        Assert.Equal(before, await SnapshotAsync(db));
    }

    private static async Task AssertOriginalSaleUnchangedAsync(InMemoryAppDbContext db, bool allocated)
    {
        var order = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == 41);
        var line = await db.OrderLines.AsNoTracking().SingleAsync(x => x.Id == 51);
        Assert.Equal(200m, order.GrandTotal); Assert.Equal(InvoiceIssuanceRoute.Automatic, order.InvoiceIssuanceRoute);
        Assert.Equal(OrderStatus.Completed, order.Status);
        Assert.Equal(2m, line.Quantity); Assert.Equal(100m, line.UnitPrice); Assert.Equal(200m, line.LineTotal);
        Assert.False(line.IsDeleted);
        if (allocated)
        {
            var allocation = await db.OrderLegalEntityAllocations.AsNoTracking().SingleAsync(x => x.Id == 61);
            Assert.Equal(2m, allocation.Quantity); Assert.Equal(2m, allocation.BaseQuantity);
            Assert.Equal(200m, allocation.NetAmount); Assert.Equal(100m, allocation.UnitPrice);
            Assert.False(allocation.IsDeleted);
        }
    }

    private static async Task<string> SnapshotAsync(InMemoryAppDbContext db) => JsonSerializer.Serialize(new
    {
        Heads = await db.InvoiceHeads.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.IsDeleted, x.TotalQuantity, x.SubTotal, x.VatAmount, x.GrandTotal, x.LastIssuanceRelevantChangeAtUtc }).ToListAsync(),
        Details = await db.InvoiceDetails.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.IsDeleted, x.Quantity, x.UnitPrice, x.VatRate, x.Amount, x.VatAmount, x.TotalAmount, x.Note, x.SourceType, x.OrderLineId, x.OrderLegalEntityAllocationId }).ToListAsync(),
        Orders = await db.Orders.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.GrandTotal, x.InvoiceIssuanceRoute, x.Status }).ToListAsync()
    });

    private static IInvoiceCommandService Service(InMemoryAppDbContext db) => new InvoiceCommandService(
        new InvoiceRepository(db), new ProductVariantRepository(db),
        new InvoiceProviderSettingRepository(db, new TestCredentialProtector()),
        new OrderLegalEntityAllocationRepository(db), new LegalEntityRepository(db), new InvoiceInputStockRepository(db));

    private static InMemoryAppDbContext CreateContext(out TenantContext tenant)
    {
        tenant = new TenantContext(); tenant.SetStore(1, "store-one");
        var options = new DbContextOptionsBuilder<InMemoryAppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        return new InMemoryAppDbContext(options, tenant, new TestCurrentUser());
    }

    private static async Task SeedAsync(InMemoryAppDbContext db, bool allocated = false, bool includeInvoice = true)
    {
        db.Stores.Add(new Store { Id = 1, Name = "store-one", SubDomain = "store-one", SubDomainNormalized = "STORE-ONE", IsActive = true });
        db.Units.Add(new Unit { Id = 1, StoreId = 1, Code = "CAI", Name = "Cái", IsActive = true });
        db.Products.Add(new Product { Id = 21, StoreId = 1, Name = "Sản phẩm nguồn", Alias = "san-pham-nguon", BaseUnitId = 1, CategoryId = 1, SupplierId = 1 });
        db.ProductVariants.Add(new ProductVariant { Id = 31, StoreId = 1, ProductId = 21, ProductVariantName = "Sản phẩm nguồn", Sku = "AUTO-DETAIL-TEST", HasInputInvoice = true, IsActive = true });
        db.InvoiceProviderSettings.Add(new InvoiceProviderSetting
        {
            Id = 11, StoreId = 1, ProviderCode = "VIETTEL", BaseUrl = "https://example.test", Username = "test-only",
            Password = "synthetic-protected-credential", SupplierTaxCode = "0100000001", TemplateCode = "1/001", InvoiceSeries = "C26AAA", InvoiceType = "1", IsActive = true
        });
        db.Orders.Add(new Order { Id = 41, StoreId = 1, OrderNumber = "TEST-AUTO-41", POSShiftId = 1, Status = OrderStatus.Completed,
            Subtotal = 200m, GrandTotal = 200m, CompletedAtUtc = OriginalChangeAt, InvoiceIssuanceRoute = InvoiceIssuanceRoute.Automatic });
        db.OrderLines.Add(new OrderLine { Id = 51, StoreId = 1, OrderId = 41, ProductId = 21, VariantId = 31, ItemName = "Sản phẩm nguồn",
            UnitName = "Cái", Quantity = 2m, BaseQuantity = 2m, Multiplier = 1m, UnitPrice = 100m, LineTotal = 200m });
        if (allocated)
        {
            db.LegalEntities.Add(new LegalEntity { Id = 21, StoreId = 1, Code = "HKD-1", Name = "HKD kiểm thử", LegalName = "HKD kiểm thử",
                TaxCode = "0100000001", InvoiceProviderSettingId = 11, IsActive = true });
            db.OrderLegalEntityAllocations.Add(new OrderLegalEntityAllocation { Id = 61, StoreId = 1, OrderId = 41, OrderLineId = 51,
                ProductVariantId = 31, LegalEntityId = 21, WarehouseId = 1, SalePriority = 1, Quantity = 2m, BaseQuantity = 2m,
                UnitPrice = 100m, LineTotal = 200m, NetAmount = 200m });
        }
        if (includeInvoice)
        {
            db.InvoiceHeads.Add(new InvoiceHead
            {
                Id = 71, StoreId = 1, OrderId = 41, LegalEntityId = allocated ? 21 : null, InvoiceProviderSettingId = 11,
                ProviderStatus = InvoiceProviderStatus.LocalDraft, TransactionUuid = Guid.NewGuid().ToString("D"),
                LastIssuanceRelevantChangeAtUtc = OriginalChangeAt, TotalQuantity = 3m, SubTotal = 250m, GrandTotal = 250m,
                Details = [
                    new InvoiceDetail { Id = 81, StoreId = 1, OrderLineId = 51, OrderLegalEntityAllocationId = allocated ? 61 : null,
                        ProductVariantId = 31, SourceType = InvoiceDetailSourceType.FromOrderLine, ItemName = "Sản phẩm nguồn", UnitName = "Cái",
                        Quantity = 2m, UnitPrice = 100m, Amount = 200m, TotalAmount = 200m },
                    new InvoiceDetail { Id = 82, StoreId = 1, SourceType = InvoiceDetailSourceType.Manual, ItemName = "Dòng thêm tay",
                        UnitName = "Cái", Quantity = 1m, UnitPrice = 50m, Amount = 50m, TotalAmount = 50m }
                ]
            });
        }
        await db.SaveChangesAsync();
    }

    private sealed class TestCredentialProtector : IInvoiceProviderCredentialProtector
    {
        public bool IsProtected(string value) => true;
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string protectedOrLegacyPlaintext) => protectedOrLegacyPlaintext;
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public int? UserId => 99;
        public string? UserName => "invoice-auto-mutation-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
