using GaoApp.Application.DTOs.Inventory.InputInvoices;
using FluentAssertions;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Repositories.LegalEntities;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace GaoApp.Tests.Inventory;

// R2.4-C2 coverage: picker owner resolution remains server-authoritative and receipt-scoped.
// R2.4-C2 coverage: picker owner resolution remains server-authoritative and receipt-scoped.
public sealed class InputInvoicePickerServiceTests
{
    [Fact]
    public void B3_association_contract_exposes_context_unlink_and_atomic_relink()
    {
        var methods = typeof(IInputInvoicePickerService).GetMethods()
            .Select(x => x.Name)
            .ToHashSet(StringComparer.Ordinal);
        var assembly = typeof(InputInvoicePickerContextDto).Assembly;

        methods.Should().Contain("GetAssociationContextAsync");
        methods.Should().Contain("RelinkAsync");
        assembly.GetType(
                "GaoApp.Application.DTOs.Inventory.InputInvoices.InputInvoiceAssociationContextDto")
            .Should().NotBeNull();
        assembly.GetType(
                "GaoApp.Application.DTOs.Inventory.InputInvoices.RelinkInputInvoiceRequest")
            .Should().NotBeNull();
        assembly.GetType(
                "GaoApp.Application.DTOs.Inventory.InputInvoices.UnlinkInputInvoiceRequest")
            .Should().NotBeNull();
    }

    [Fact]
    public void Picker_contract_should_use_receipt_and_logical_key_only()
    {
        var methods = typeof(IInputInvoicePickerService).GetMethods();

        Assert.Contains(methods, x => x.Name == "BrowseAsync");
        Assert.Contains(methods, x => x.Name == "GetPdfAsync");
        Assert.Contains(methods, x => x.Name == "GetXmlPreviewAsync");
        Assert.Contains(methods, x => x.Name == "GetLinkedPdfAsync");
        Assert.Contains(methods, x => x.Name == "GetLinkedXmlPreviewAsync");
        Assert.Contains(methods, x => x.Name == "UnlinkAsync");
        Assert.Contains(methods, x => x.Name == "SelectAsync");
        Assert.Contains(methods, x => x.Name == "BrowseForSupplierAsync");
        Assert.Contains(methods, x => x.Name == "BrowseForSupplierAndWarehouseAsync");
        Assert.Contains(methods, x => x.Name == "GetPdfForSupplierAsync");
        Assert.Contains(methods, x => x.Name == "GetXmlPreviewForSupplierAsync");
        Assert.Contains(methods, x => x.Name == "GetXmlPreviewForSupplierAndWarehouseAsync");

        var requestProperties = typeof(SelectInputInvoiceDocumentRequest)
            .GetProperties()
            .Select(x => x.Name)
            .ToArray();
        Assert.Equal(["DocumentKey"], requestProperties);
    }

    [Fact]
    public void Split_supplier_context_contract_still_exposes_only_logical_document_keys()
    {
        var methods = typeof(IInputInvoicePickerService).GetMethods()
            .Where(x => x.Name.Contains("ForSupplier", StringComparison.Ordinal))
            .ToArray();

        methods.Should().HaveCount(5);
        methods.SelectMany(x => x.GetParameters())
            .Should().NotContain(x => x.Name != null &&
                x.Name.Contains("path", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Outward_candidate_contract_should_not_expose_a_physical_path()
    {
        var names = typeof(InputInvoicePickerCandidateDto)
            .GetProperties()
            .Select(x => x.Name)
            .ToArray();

        Assert.DoesNotContain(names, x =>
            x.Contains("Path", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("DocumentKey", names);
        Assert.Contains("LinkedCurrentReceipt", names);
        Assert.Contains("LinkedOtherReceiptCount", names);
    }

    [Fact]
    public async Task Browse_should_apply_fixed_receipt_supplier_and_batch_link_status()
    {
        var tenant = new TenantContext();
        tenant.SetHostAdmin();
        var options = new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var context = new InMemoryAppDbContext(options, tenant, new UserStub());
        context.Stores.Add(new Store
        {
            Id = 1, Name = "Store", SubDomain = "store",
            SubDomainNormalized = "STORE", IsActive = true
        });
        context.LegalEntities.Add(new LegalEntity
        {
            Id = 2, StoreId = 1, Code = "LE", Name = "LE", LegalName = "LE",
            TaxCode = "0101234567", IsActive = true
        });
        context.Warehouses.Add(new Warehouse
        {
            Id = 3, StoreId = 1, LegalEntityId = 2, Code = "WH", Name = "WH", IsActive = true
        });
        context.Suppliers.Add(new Supplier
        {
            Id = 4, StoreId = 1, Code = "SUP", Name = "Supplier",
            TaxCode = "031.277-0607", IsActive = true
        });
        context.StockDocuments.AddRange(
            Receipt(10, "PN-10"), Receipt(11, "PN-11"), Receipt(12, "PN-12"));
        var invoice = new InputInvoiceHead
        {
            Id = 20, StoreId = 1, SellerTaxCode = "0312770607",
            InvoiceSeries = "C26MVP", InvoiceNumber = "7941",
            InvoiceDate = new DateTime(2026, 7, 2)
        };
        InputInvoiceIdentityPolicy.ApplyRequiredIdentity(invoice);
        context.InputInvoiceHeads.Add(invoice);
        context.StockDocumentInputInvoiceMaps.AddRange(
            new StockDocumentInputInvoiceMap { StoreId = 1, StockDocumentId = 10, InputInvoiceHeadId = 20 },
            new StockDocumentInputInvoiceMap { StoreId = 1, StockDocumentId = 11, InputInvoiceHeadId = 20 });
        await context.SaveChangesAsync();
        tenant.SetStore(1, "store");
        context.ChangeTracker.Clear();

        var picker = new InputInvoicePickerService(
            new InputInvoiceRepository(context),
            new LibraryStub(),
            new InputInvoiceXmlDocumentParser(),
            DispatchProxy.Create<IInputInvoiceXmlService, ThrowingXmlServiceProxy>());

        var result = await picker.BrowseAsync(
            1, 10, new InputInvoicePickerBrowseRequest { Year = 2026, Month = 7 });

        Assert.Equal(4, result.Context.SupplierId);
        Assert.Equal("031.277-0607", result.Context.SupplierTaxCode);
        var candidate = Assert.Single(result.Candidates);
        Assert.True(candidate.LinkedCurrentReceipt);
        Assert.Equal(1, candidate.LinkedOtherReceiptCount);
        Assert.False(candidate.SelectionAllowed);
        Assert.Equal("AlreadyLinkedCurrentReceipt", candidate.SelectionBlockReasonCode);

        var otherReceiptResult = await picker.BrowseAsync(
            1, 12, new InputInvoicePickerBrowseRequest { Year = 2026, Month = 7 });
        var otherReceiptCandidate = Assert.Single(otherReceiptResult.Candidates);
        Assert.False(otherReceiptCandidate.LinkedCurrentReceipt);
        Assert.Equal(2, otherReceiptCandidate.LinkedOtherReceiptCount);
        Assert.True(otherReceiptCandidate.SelectionAllowed);
    }

    [Fact]
    public async Task Pending_selection_uses_the_normal_import_and_link_pipeline()
    {
        var tenant = new TenantContext();
        tenant.SetHostAdmin();
        var options = new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var context = new InMemoryAppDbContext(options, tenant, new UserStub());
        context.Stores.Add(new Store
        {
            Id = 1, Name = "Store", SubDomain = "store",
            SubDomainNormalized = "STORE", IsActive = true
        });
        context.LegalEntities.Add(new LegalEntity
        {
            Id = 2, StoreId = 1, Code = "LE", Name = "LE", LegalName = "LE",
            TaxCode = "0101234567", IsActive = true
        });
        context.Warehouses.Add(new Warehouse
        {
            Id = 3, StoreId = 1, LegalEntityId = 2, Code = "WH", Name = "WH",
            IsActive = true
        });
        context.Suppliers.Add(new Supplier
        {
            Id = 4, StoreId = 1, Code = "SUP", Name = "Supplier",
            TaxCode = "0312770607", IsActive = true
        });
        context.StockDocuments.Add(Receipt(10, "PN-10"));
        await context.SaveChangesAsync();
        tenant.SetStore(1, "store");
        context.ChangeTracker.Clear();
        var xml = DispatchProxy.Create<IInputInvoiceXmlService, RecordingXmlServiceProxy>();
        var recording = (RecordingXmlServiceProxy)(object)xml;
        var picker = new InputInvoicePickerService(
            new InputInvoiceRepository(context),
            new LibraryStub(),
            new InputInvoiceXmlDocumentParser(),
            xml);

        var result = await picker.SelectAsync(1, 10, new()
        {
            DocumentKey = "2026:07:invoice"
        });

        recording.ImportCalls.Should().Be(1);
        recording.StoreId.Should().Be(1);
        recording.StockDocumentId.Should().Be(10);
        result.InputInvoiceHeadId.Should().Be(20);
    }

    [Fact]
    public async Task Linked_preview_should_require_current_relation_and_resolve_transient_identity()
    {
        var tenant = new TenantContext();
        tenant.SetHostAdmin();
        var options = new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var context = new InMemoryAppDbContext(options, tenant, new UserStub());
        context.Stores.Add(new Store
        {
            Id = 1, Name = "Store", SubDomain = "store",
            SubDomainNormalized = "STORE", IsActive = true
        });
        context.LegalEntities.Add(new LegalEntity
        {
            Id = 2, StoreId = 1, Code = "LE", Name = "LE", LegalName = "LE",
            TaxCode = "0101234567", IsActive = true
        });
        context.Warehouses.Add(new Warehouse
        {
            Id = 3, StoreId = 1, LegalEntityId = 2, Code = "WH", Name = "WH", IsActive = true
        });
        context.Suppliers.Add(new Supplier
        {
            Id = 4, StoreId = 1, Code = "SUP", Name = "Supplier",
            TaxCode = "0312770607", IsActive = true
        });
        context.StockDocuments.Add(Receipt(10, "PN-10"));
        var linked = Invoice(20, "7941");
        var unlinked = Invoice(21, "7942");
        context.InputInvoiceHeads.AddRange(linked, unlinked);
        context.StockDocumentInputInvoiceMaps.Add(new StockDocumentInputInvoiceMap
        {
            StoreId = 1, StockDocumentId = 10, InputInvoiceHeadId = 20
        });
        await context.SaveChangesAsync();
        tenant.SetStore(1, "store");
        context.ChangeTracker.Clear();
        var ownerResolver = new MutableOwnerResolverStub
        {
            Status = InputInvoiceBuyerOwnerResolutionStatus.Resolved,
            LegalEntityId = 2,
            LegalEntityName = "LE"
        };
        var picker = CreatePicker(context, ownerResolver);

        var pdf = await picker.GetLinkedPdfAsync(1, 10, 20);
        Assert.Equal([1, 2, 3], pdf.Content);
        var xml = await picker.GetLinkedXmlPreviewAsync(1, 10, 20);
        Assert.Equal("C26MVP", xml.InvoiceSeries);
        Assert.Equal("7941", xml.InvoiceNumber);
        Assert.Equal(InputInvoiceBuyerOwnerResolutionStatus.Resolved.ToString(),
            xml.BuyerOwnerResolutionStatus);
        Assert.Equal(2, xml.ResolvedBuyerLegalEntityId);
        Assert.True(xml.BuyerOwnerMatchesReceipt);

        ownerResolver.Status = InputInvoiceBuyerOwnerResolutionStatus.NotFound;
        ownerResolver.LegalEntityId = null;
        ownerResolver.LegalEntityName = null;

        var driftedPreview = await picker.GetLinkedXmlPreviewAsync(1, 10, 20);
        Assert.Equal(InputInvoiceBuyerOwnerResolutionStatus.NotFound.ToString(),
            driftedPreview.BuyerOwnerResolutionStatus);
        Assert.False(driftedPreview.BuyerOwnerMatchesReceipt);

        var xmlService = (IInputInvoiceXmlService)Activator.CreateInstance(
            typeof(InputInvoiceXmlService),
            new InputInvoiceRepository(context),
            null,
            new InputInvoiceXmlDocumentParser(),
            null,
            null,
            ownerResolver,
            null,
            null)!;
        var cards = await xmlService.GetInvoicesByStockDocumentAsync(1, 10);
        var card = Assert.Single(cards);
        Assert.Equal(InputInvoiceBuyerOwnerResolutionStatus.NotFound.ToString(),
            card.BuyerOwnerResolutionStatus);
        Assert.Equal(false, card.GetType().GetProperty("BuyerOwnerMatchesReceipt")?.GetValue(card));
        Assert.Equal("BuyerOwnerNotFound",
            card.GetType().GetProperty("OwnerWarningReasonCode")?.GetValue(card));
        await Assert.ThrowsAsync<BusinessRuleException>(
            () => picker.GetLinkedPdfAsync(1, 10, 21));
    }

    [Fact]
    public async Task Unlink_should_remove_only_target_relation_maps_and_audit_once()
    {
        var tenant = new TenantContext();
        tenant.SetHostAdmin();
        var options = new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var context = new InMemoryAppDbContext(options, tenant, new UserStub());
        context.Stores.Add(new Store
        {
            Id = 1, Name = "Store", SubDomain = "store",
            SubDomainNormalized = "STORE", IsActive = true
        });
        context.LegalEntities.Add(new LegalEntity
        {
            Id = 2, StoreId = 1, Code = "LE", Name = "LE", LegalName = "LE", IsActive = true
        });
        context.Warehouses.Add(new Warehouse
        {
            Id = 3, StoreId = 1, LegalEntityId = 2, Code = "WH", Name = "WH", IsActive = true
        });
        context.Suppliers.Add(new Supplier
        {
            Id = 4, StoreId = 1, Code = "SUP", Name = "Supplier",
            TaxCode = "0312770607", IsActive = true
        });
        var current = Receipt(10, "PN-10");
        current.Status = StockDocumentStatus.Confirmed;
        var otherReceipt = Receipt(11, "PN-11");
        context.StockDocuments.AddRange(current, otherReceipt);
        context.StockDocumentLines.AddRange(
            new StockDocumentLine
            {
                Id = 101, StockDocumentId = 10, LineNo = 1,
                ProductVariantId = 1, ProductNameSnapshot = "A",
                Quantity = 2, Factor = 1, BaseQuantity = 2, UnitCost = 5, LineTotal = 10
            },
            new StockDocumentLine
            {
                Id = 102, StockDocumentId = 10, LineNo = 2,
                ProductVariantId = 1, ProductNameSnapshot = "B",
                Quantity = 3, Factor = 1, BaseQuantity = 3, UnitCost = 6, LineTotal = 18
            });
        var target = Invoice(20, "7941");
        target.Details.Add(new InputInvoiceDetail
        {
            Id = 201, LineNo = 1, ItemName = "A", Quantity = 2, UnitPrice = 5, LineAmount = 10
        });
        context.InputInvoiceHeads.Add(target);
        context.StockDocumentInputInvoiceMaps.AddRange(
            new StockDocumentInputInvoiceMap { StoreId = 1, StockDocumentId = 10, InputInvoiceHeadId = 20 },
            new StockDocumentInputInvoiceMap { StoreId = 1, StockDocumentId = 11, InputInvoiceHeadId = 20 });
        context.StockDocumentLineInputInvoiceMaps.Add(
            new StockDocumentLineInputInvoiceMap
            {
                StoreId = 1, StockDocumentId = 10, StockDocumentLineId = 101,
                InputInvoiceDetailId = 201, UseInputInvoice = true
            });
        await context.SaveChangesAsync();
        tenant.SetStore(1, "store");
        context.ChangeTracker.Clear();
        var reconciliation = DispatchProxy.Create<
            IInputInvoiceReconciliationService, RecordingReconciliationProxy>();
        var recording = (RecordingReconciliationProxy)(object)reconciliation;
        var picker = CreatePicker(context, reconciliationService: reconciliation);

        var first = await picker.UnlinkAsync(1, 10, new UnlinkInputInvoiceRequest
        {
            ExpectedCurrentInputInvoiceHeadId = 20,
            Reason = "XML không đúng chứng từ"
        });
        var second = await picker.UnlinkAsync(1, 10, new UnlinkInputInvoiceRequest
        {
            ExpectedCurrentInputInvoiceHeadId = 20,
            Reason = "Yêu cầu lặp lại"
        });
        Assert.Equal(InputInvoiceAssociationMutationOutcomes.Applied, first.Outcome);
        Assert.Equal(InputInvoiceAssociationMutationOutcomes.AlreadyApplied, second.Outcome);
        Assert.Equal(1, recording.InvalidateCalls);

        Assert.False(await context.StockDocumentInputInvoiceMaps.AnyAsync(x =>
            x.StockDocumentId == 10 && x.InputInvoiceHeadId == 20));
        Assert.True(await context.StockDocumentInputInvoiceMaps.AnyAsync(x =>
            x.StockDocumentId == 11 && x.InputInvoiceHeadId == 20));
        Assert.False(await context.StockDocumentLineInputInvoiceMaps.AnyAsync(x =>
            x.StockDocumentId == 10 && x.InputInvoiceDetailId == 201));
        Assert.Equal(1, await context.InputInvoiceHeads.CountAsync());
        Assert.Equal(1, await context.InputInvoiceDetails.CountAsync());
        Assert.Equal(StockDocumentStatus.Confirmed,
            await context.StockDocuments.Where(x => x.Id == 10).Select(x => x.Status).SingleAsync());
        Assert.Equal(4,
            await context.StockDocuments.Where(x => x.Id == 10).Select(x => x.SupplierId).SingleAsync());
        var audit = await context.PurchaseReceiptAuditEvents.SingleAsync();
        Assert.Equal(PurchaseReceiptAuditEventType.InputInvoiceUnlinked, audit.EventType);
        Assert.Equal(602, audit.ActorUserId);
        Assert.Equal("XML không đúng chứng từ", audit.Reason);
    }

    private static InputInvoicePickerService CreatePicker(
        InMemoryAppDbContext context,
        IInputInvoiceBuyerOwnerResolutionService? ownerResolver = null,
        IInputInvoiceReconciliationService? reconciliationService = null) => new(
        new InputInvoiceRepository(context),
        new LibraryStub(),
        new InputInvoiceXmlDocumentParser(),
        DispatchProxy.Create<IInputInvoiceXmlService, ThrowingXmlServiceProxy>(),
        ownerResolver,
        reconciliationService);

    private static InputInvoiceHead Invoice(int id, string number)
    {
        var invoice = new InputInvoiceHead
        {
            Id = id, StoreId = 1, SellerTaxCode = "0312770607",
            InvoiceSeries = "C26MVP", InvoiceNumber = number,
            InvoiceDate = new DateTime(2026, 7, 2),
            BuyerTaxCode = "0101234567"
        };
        InputInvoiceIdentityPolicy.ApplyRequiredIdentity(invoice);
        return invoice;
    }

    private static StockDocument Receipt(int id, string number) => new()
    {
        Id = id, StoreId = 1, DocumentNo = number,
        Type = StockDocumentType.Receipt,
        Status = StockDocumentStatus.PendingApproval,
        ReceiptSource = PurchaseReceiptSource.Direct,
        WarehouseId = 3, SupplierId = 4
    };

    private sealed class LibraryStub : IInputInvoiceDocumentLibrary
    {
        public Task<IReadOnlyList<InputInvoicePickerCandidateDto>> BrowseAsync(
            string normalizedSupplierTaxCode, InputInvoicePickerBrowseRequest request,
            CancellationToken ct = default)
        {
            Assert.Equal("0312770607", normalizedSupplierTaxCode);
            IReadOnlyList<InputInvoicePickerCandidateDto> result =
            [
                new()
                {
                    DocumentKey = "2026:07:invoice",
                    InvoiceDate = new DateTime(2026, 7, 2),
                    InvoiceSeries = "C26MVP", InvoiceNumber = "7941",
                    SellerTaxCode = "0312770607", XmlValid = true,
                    HasPdf = true, HasXml = true, SelectionAllowed = true
                }
            ];
            return Task.FromResult(result);
        }

        public async Task<InputInvoicePickerCandidateDto> ResolveAsync(string normalizedSupplierTaxCode, string documentKey, CancellationToken ct = default) =>
            (await BrowseAsync(normalizedSupplierTaxCode, new InputInvoicePickerBrowseRequest(), ct)).Single();
        public Task<InputInvoicePdfPreviewDto> GetPdfAsync(string normalizedSupplierTaxCode, string documentKey, CancellationToken ct = default) =>
            Task.FromResult(new InputInvoicePdfPreviewDto { Content = [1, 2, 3], FileName = "invoice.pdf" });
        public Task<byte[]> GetXmlBytesAsync(string normalizedSupplierTaxCode, string documentKey, CancellationToken ct = default) =>
            Task.FromResult(System.Text.Encoding.UTF8.GetBytes("""
                <HDon><DLHDon><TTChung><KHMSHDon>1</KHMSHDon><KHHDon>C26MVP</KHHDon><SHDon>7941</SHDon><NLap>2026-07-02</NLap></TTChung><NDHDon><NBan><Ten>Supplier</Ten><MST>0312770607</MST></NBan><NMua><Ten>Buyer</Ten><MST>0101234567</MST></NMua><DSHHDVu><HHDVu><STT>1</STT><THHDVu>Rice</THHDVu><DVTinh>kg</DVTinh><SLuong>1</SLuong><DGia>10</DGia><ThTien>10</ThTien></HHDVu></DSHHDVu><TToan><TgTCThue>10</TgTCThue><TgTThue>0</TgTThue><TgTTTBSo>10</TgTTTBSo></TToan></NDHDon></DLHDon></HDon>
                """));
    }

    private class ThrowingXmlServiceProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new NotSupportedException(targetMethod?.Name);
    }

    private class RecordingXmlServiceProxy : DispatchProxy
    {
        public int ImportCalls { get; private set; }
        public int StoreId { get; private set; }
        public int StockDocumentId { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IInputInvoiceXmlService.ImportAndLinkAsync))
            {
                ImportCalls++;
                StoreId = (int)args![0]!;
                StockDocumentId = (int)args[1]!;
                return Task.FromResult(new InputInvoicePickerSelectionResultDto
                {
                    InputInvoiceHeadId = 20,
                    StockDocumentId = StockDocumentId
                });
            }

            throw new NotSupportedException(targetMethod?.Name);
        }
    }

    private class RecordingReconciliationProxy : DispatchProxy
    {
        public int InvalidateCalls { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IInputInvoiceReconciliationService
                    .InvalidateWithinTransactionAsync))
            {
                InvalidateCalls++;
                return Task.CompletedTask;
            }
            throw new NotSupportedException(targetMethod?.Name);
        }
    }

    private sealed class MutableOwnerResolverStub : IInputInvoiceBuyerOwnerResolutionService
    {
        public InputInvoiceBuyerOwnerResolutionStatus Status { get; set; }
        public int? LegalEntityId { get; set; }
        public string? LegalEntityName { get; set; }
        public int Calls { get; private set; }

        public Task<InputInvoiceBuyerOwnerResolution> ResolveWithinTransactionAsync(
            int storeId,
            InputInvoiceHead invoice,
            CancellationToken ct = default)
        {
            Calls++;
            invoice.BuyerOwnerResolutionStatus = Status;
            invoice.ResolvedBuyerLegalEntityId = LegalEntityId;
            invoice.BuyerOwnerResolutionUpdatedAtUtc = DateTime.UtcNow;
            return Task.FromResult(new InputInvoiceBuyerOwnerResolution(
                Status,
                InputInvoiceIdentityPolicy.NormalizeTaxCode(invoice.BuyerTaxCode),
                LegalEntityId,
                LegalEntityId.HasValue ? "LE" : null,
                LegalEntityName,
                LegalEntityId.HasValue ? 1 : 0));
        }
    }

    private sealed class UserStub : ICurrentUser
    {
        public int? UserId => 602;
        public string? UserName => "picker";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
