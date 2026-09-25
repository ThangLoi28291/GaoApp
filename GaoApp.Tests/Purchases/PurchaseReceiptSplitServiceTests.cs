using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.Purchases;

// R2.4-C2 coverage: split targets are owner-validated before the first aggregate mutation.
public sealed class PurchaseReceiptSplitServiceTests
{
    [Fact]
    public async Task Two_result_partial_split_writes_reconstructable_source_and_child_lineage()
    {
        var auditEvents = await ExecuteSplitAsync([6m, 4m]);

        auditEvents.Should().HaveCount(2);
        var sourceEvent = auditEvents[0];
        sourceEvent.EventType.Should().Be(PurchaseReceiptAuditEventType.ReceiptSplitSource);
        sourceEvent.StockDocumentId.Should().Be(100);

        var sourceOldValues = Parse(sourceEvent.OldValuesJson);
        sourceOldValues.GetProperty("SourceReceiptId").GetInt32().Should().Be(100);
        sourceOldValues.GetProperty("SourceDocumentNo").GetString().Should().Be("NK-SOURCE");

        var sourceNewValues = Parse(sourceEvent.NewValuesJson);
        sourceNewValues.GetProperty("ResultCount").GetInt32().Should().Be(2);
        var splitResults = sourceNewValues.GetProperty("SplitResults")
            .EnumerateArray().ToArray();
        splitResults.Select(x => x.GetProperty("TargetIndex").GetInt32())
            .Should().Equal(1, 2);
        splitResults.Select(x => x.GetProperty("ResultReceiptId").GetInt32())
            .Should().Equal(100, 201);
        splitResults.Select(x => x.GetProperty("ResultDocumentNo").GetString())
            .Should().Equal("NK-SOURCE", "NK-20260824-0021");
        splitResults.Select(x => x.GetProperty("LineAllocations")[0]
                .GetProperty("BaseQuantity").GetDecimal())
            .Should().Equal(6m, 4m);
        splitResults.SelectMany(x => x.GetProperty("LineAllocations").EnumerateArray())
            .Should().OnlyContain(x => x.GetProperty("SourceLineId").GetInt32() == 10);
        splitResults.Sum(x => x.GetProperty("LineAllocations")[0]
                .GetProperty("BaseQuantity").GetDecimal())
            .Should().Be(10m);

        var childEvent = auditEvents[1];
        childEvent.EventType.Should().Be(PurchaseReceiptAuditEventType.ReceiptSplitChild);
        childEvent.StockDocumentId.Should().Be(201);
        var childValues = Parse(childEvent.NewValuesJson);
        childValues.GetProperty("SourceReceiptId").GetInt32().Should().Be(100);
        childValues.GetProperty("SourceDocumentNo").GetString().Should().Be("NK-SOURCE");
        childValues.GetProperty("ResultReceiptId").GetInt32().Should().Be(201);
        childValues.GetProperty("ResultDocumentNo").GetString()
            .Should().Be("NK-20260824-0021");
        childValues.GetProperty("TargetIndex").GetInt32().Should().Be(2);
        childValues.GetProperty("LineAllocations")[0]
            .GetProperty("SourceLineId").GetInt32().Should().Be(10);
        childValues.GetProperty("LineAllocations")[0]
            .GetProperty("BaseQuantity").GetDecimal().Should().Be(4m);
    }

    [Fact]
    public async Task Three_result_split_orders_every_result_and_child_lineage_deterministically()
    {
        var auditEvents = await ExecuteSplitAsync([1m, 1m, 1m], reverseTargets: true);

        auditEvents.Should().HaveCount(3);
        var sourceValues = Parse(auditEvents[0].NewValuesJson);
        sourceValues.GetProperty("ResultCount").GetInt32().Should().Be(3);
        var splitResults = sourceValues.GetProperty("SplitResults")
            .EnumerateArray().ToArray();
        splitResults.Select(x => x.GetProperty("TargetIndex").GetInt32())
            .Should().Equal(1, 2, 3);
        splitResults.Select(x => x.GetProperty("ResultReceiptId").GetInt32())
            .Should().Equal(100, 201, 202);
        splitResults.SelectMany(x => x.GetProperty("LineAllocations").EnumerateArray())
            .Select(x => x.GetProperty("BaseQuantity").GetDecimal())
            .Should().Equal(1m, 1m, 1m);

        var childValues = auditEvents.Skip(1).Select(x => Parse(x.NewValuesJson)).ToArray();
        childValues.Select(x => x.GetProperty("TargetIndex").GetInt32())
            .Should().Equal(2, 3);
        childValues.Select(x => x.GetProperty("ResultReceiptId").GetInt32())
            .Should().Equal(201, 202);
        childValues.Select(x => x.GetProperty("ResultDocumentNo").GetString())
            .Should().Equal("NK-20260824-0021", "NK-20260824-0022");
        childValues.Select(x => x.GetProperty("LineAllocations")[0]
                .GetProperty("SourceLineId").GetInt32())
            .Should().Equal(10, 10);
        childValues.Select(x => x.GetProperty("LineAllocations")[0]
                .GetProperty("BaseQuantity").GetDecimal())
            .Should().Equal(1m, 1m);
    }

    [Fact]
    public async Task Split_retaining_existing_invoice_preserves_supplier_for_central_link_binding()
    {
        var auditEvents = await ExecuteSplitAsync(
            [6m, 4m],
            retainExistingInvoice: true);

        auditEvents.Should().HaveCount(2);
    }

    [Fact]
    public void Split_command_contract_preserves_pending_direct_scope_and_atomic_order()
    {
        var source = Read("GaoApp.Application/Services/Inventory/StockDocumentSplitService.cs");

        source.Should().Contain("PurchaseReceiptSource.Direct");
        source.Should().Contain("StockDocumentStatus.PendingApproval");
        source.Should().Contain("PurchaseOrderId.HasValue");
        source.Should().Contain("BeginSupplierResolutionTransactionAsync");
        source.Should().Contain("LockReceiptAggregateForSplitAsync");
        source.Should().Contain("GetNextNumberAsync");
        source.Should().Contain("CommitSupplierResolutionTransactionAsync");
        source.Should().Contain("RollbackSupplierResolutionTransactionAsync");
    }

    [Fact]
    public void Original_is_result_one_children_inherit_date_and_remain_pending()
    {
        var source = Read("GaoApp.Application/Services/Inventory/StockDocumentSplitService.cs");

        source.Should().Contain("target.TargetIndex == 1");
        source.Should().Contain("DocumentDate = source.DocumentDate");
        source.Should().Contain("Status = StockDocumentStatus.PendingApproval");
        source.Should().Contain("SubmittedAtUtc = splitNowUtc");
    }

    [Fact]
    public void Split_command_has_no_posting_payable_or_purchase_order_side_effect_calls()
    {
        var source = Read("GaoApp.Application/Services/Inventory/StockDocumentSplitService.cs");

        source.Should().NotContain("IInventoryMovementService");
        source.Should().NotContain("IInventoryRevaluationService");
        source.Should().NotContain("CreatePayablesIfNeededAsync");
        source.Should().NotContain("ApplyApprovedReceiptToPurchaseOrder");
        source.Should().NotContain("ApproveAsync(");
    }

    [Fact]
    public void Payment_and_snapshot_validation_precede_mutation()
    {
        var source = Read("GaoApp.Application/Services/Inventory/StockDocumentSplitService.cs");
        var validate = source.IndexOf("ValidateSnapshot", StringComparison.Ordinal);
        var payment = source.IndexOf("PurchaseReceiptSplitPolicy.Build", StringComparison.Ordinal);
        var mutate = source.IndexOf("ApplyTargetToSource", StringComparison.Ordinal);

        validate.Should().BeGreaterThanOrEqualTo(0);
        payment.Should().BeGreaterThan(validate);
        mutate.Should().BeGreaterThan(payment);
    }

    private static async Task<IReadOnlyList<PurchaseReceiptAuditEvent>> ExecuteSplitAsync(
        IReadOnlyList<decimal> allocations,
        bool reverseTargets = false,
        bool retainExistingInvoice = false)
    {
        var supplier = new Supplier
        {
            Id = 6,
            StoreId = 1,
            Code = "SUP-01",
            Name = "Supplier 01",
            TaxCode = "0101234567"
        };
        var warehouse = new Warehouse
        {
            Id = 5,
            StoreId = 1,
            LegalEntityId = 7,
            Code = "WH-01",
            Name = "Warehouse 01"
        };
        var sourceLine = new StockDocumentLine
        {
            Id = 10,
            LineNo = 1,
            ProductVariantId = 30,
            ProductNameSnapshot = "Product 01",
            Factor = 1m,
            Quantity = allocations.Sum(),
            BaseQuantity = allocations.Sum(),
            UnitCost = 10m,
            LineTotal = allocations.Sum() * 10m,
            RowVersion = [4, 5, 6]
        };
        var source = new StockDocument
        {
            Id = 100,
            StoreId = 1,
            DocumentNo = "NK-SOURCE",
            Type = StockDocumentType.Receipt,
            Status = StockDocumentStatus.PendingApproval,
            ReceiptSource = PurchaseReceiptSource.Direct,
            DocumentDate = new DateTime(2026, 8, 24),
            WarehouseId = warehouse.Id,
            Warehouse = warehouse,
            SupplierId = supplier.Id,
            Supplier = supplier,
            RowVersion = [1, 2, 3]
        };
        source.Lines.Add(sourceLine);

        InputInvoiceHead? existingInvoice = null;
        if (retainExistingInvoice)
        {
            existingInvoice = new InputInvoiceHead
            {
                Id = 70,
                StoreId = 1,
                SellerTaxCode = supplier.TaxCode,
                NormalizedSellerTaxCode = supplier.TaxCode,
                ResolvedSupplierId = supplier.Id,
                SupplierResolutionStatus = InputInvoiceSupplierResolutionStatus.Resolved
            };
            source.InputInvoiceMaps.Add(new StockDocumentInputInvoiceMap
            {
                Id = 80,
                StoreId = 1,
                StockDocumentId = source.Id,
                InputInvoiceHeadId = existingInvoice.Id,
                InputInvoiceHead = existingInvoice
            });
        }

        var auditEvents = new List<PurchaseReceiptAuditEvent>();
        var supplierResolutionReceiptReads = 0;
        var inputInvoices = CreateProxy<IInputInvoiceRepository>((method, args) =>
            method.Name switch
            {
                nameof(IInputInvoiceRepository.BeginSupplierResolutionTransactionAsync) =>
                    Task.CompletedTask,
                nameof(IInputInvoiceRepository.CommitSupplierResolutionTransactionAsync) =>
                    Task.CompletedTask,
                nameof(IInputInvoiceRepository.RollbackSupplierResolutionTransactionAsync) =>
                    Task.CompletedTask,
                nameof(IInputInvoiceRepository.LockReceiptAggregateForSplitAsync) =>
                    Task.FromResult<StockDocument?>(source),
                nameof(IInputInvoiceRepository.GetSplitSupplierAsync) =>
                    Task.FromResult<Supplier?>(supplier),
                nameof(IInputInvoiceRepository.GetSplitWarehouseAsync) =>
                    Task.FromResult<Warehouse?>(warehouse),
                nameof(IInputInvoiceRepository.GetReceiptForSupplierResolutionAsync) =>
                    ReturnSupplierResolutionReceipt(source, ref supplierResolutionReceiptReads),
                nameof(IInputInvoiceRepository.LockForSupplierResolutionAsync) =>
                    Task.FromResult(existingInvoice),
                nameof(IInputInvoiceRepository.EnsureSingleReceiptInvoiceMapAsync) =>
                    Task.FromResult(false),
                nameof(IInputInvoiceRepository.AddMissingLineMapsAsync) => Task.CompletedTask,
                nameof(IInputInvoiceRepository.ResetReceiptLineMapsAsync) => Task.FromResult(0),
                nameof(IInputInvoiceRepository.AddPurchaseReceiptAuditEventAsync) =>
                    CaptureAudit(auditEvents, (PurchaseReceiptAuditEvent)args![0]!),
                _ => throw new InvalidOperationException($"Unexpected input-invoice call: {method.Name}")
            });

        var nextChildId = 201;
        var children = new List<StockDocument>();
        var stockDocuments = CreateProxy<IStockDocumentRepository>((method, args) =>
            method.Name switch
            {
                nameof(IStockDocumentRepository.AddAsync) =>
                    AddChild((StockDocument)args![0]!, nextChildId++, children),
                nameof(IStockDocumentRepository.GetSupplierAsync) =>
                    Task.FromResult<Supplier?>(supplier),
                nameof(IStockDocumentRepository.SaveChangesAsync) => Task.CompletedTask,
                nameof(IStockDocumentRepository.RemoveLineAsync) => Task.CompletedTask,
                _ => throw new InvalidOperationException($"Unexpected stock-document call: {method.Name}")
            });
        var nextSequence = 21;
        var numbers = CreateProxy<IDocumentNumberSequenceRepository>((method, _) =>
            method.Name == nameof(IDocumentNumberSequenceRepository.GetNextNumberAsync)
                ? Task.FromResult(nextSequence++)
                : throw new InvalidOperationException($"Unexpected sequence call: {method.Name}"));
        var reconciliationCalls = new List<(string Method, int ReceiptId)>();
        var reconciliation = CreateProxy<IInputInvoiceReconciliationService>((method, args) =>
        {
            var receiptId = (int)args![1]!;
            reconciliationCalls.Add((method.Name, receiptId));
            return method.Name switch
            {
                nameof(IInputInvoiceReconciliationService.InvalidateWithinTransactionAsync) =>
                    Task.CompletedTask,
                nameof(IInputInvoiceReconciliationService.RefreshWithinTransactionAsync) =>
                    Task.FromResult(new GaoApp.Application.DTOs.Inventory.InputInvoices.InputInvoiceReconciliationDto
                    {
                        StockDocumentId = receiptId,
                        State = InputInvoiceReconciliationState.NotApplicable
                    }),
                _ => throw new InvalidOperationException(method.Name)
            };
        });

        var ownerGuardCalls = 0;
        var ownerGuard = CreateProxy<IInputInvoiceReceiptOwnerGuard>((method, _) =>
        {
            if (method.Name != nameof(IInputInvoiceReceiptOwnerGuard.ValidateLinkWithinTransactionAsync))
                throw new InvalidOperationException(method.Name);
            ownerGuardCalls++;
            return Task.FromResult(new InputInvoiceReceiptOwnerDecision(
                warehouse.LegalEntityId,
                warehouse.LegalEntityId,
                false));
        });
        var supplierResolution = retainExistingInvoice
            ? new InputInvoiceSupplierResolutionService(
                inputInvoices, new CurrentUserStub())
            : CreateThrowingProxy<IInputInvoiceSupplierResolutionService>();
        var linkService = retainExistingInvoice
            ? new InputInvoiceReceiptLinkService(
                inputInvoices, ownerGuard, supplierResolution, reconciliation)
            : null;

        var service = new StockDocumentSplitService(
            inputInvoices,
            stockDocuments,
            numbers,
            CreateThrowingProxy<IInputInvoiceDocumentLibrary>(),
            CreateThrowingProxy<IInputInvoiceXmlService>(),
            supplierResolution,
            new TenantContextStub(),
            new CurrentUserStub(),
            ownerGuard: retainExistingInvoice ? ownerGuard : null,
            linkService: linkService,
            reconciliation: reconciliation);

        var targets = allocations.Select((quantity, index) =>
            new PurchaseReceiptSplitTargetRequest
            {
                TargetIndex = index + 1,
                WarehouseId = warehouse.Id,
                SupplierId = supplier.Id,
                ExistingInputInvoiceHeadId = retainExistingInvoice && index == 0
                    ? existingInvoice!.Id
                    : null,
                Allocations =
                [
                    new PurchaseReceiptSplitLineAllocationDto
                    {
                        SourceLineId = sourceLine.Id,
                        BaseQuantity = quantity
                    }
                ]
            }).ToList();
        if (reverseTargets)
            targets.Reverse();

        await service.SplitAsync(source.Id, new PurchaseReceiptSplitRequest
        {
            SourceRowVersion = Convert.ToBase64String(source.RowVersion),
            SourceInvoiceHeadId = existingInvoice?.Id,
            SourceLines =
            [
                new PurchaseReceiptSplitLineSnapshotDto
                {
                    SourceLineId = sourceLine.Id,
                    BaseQuantity = sourceLine.BaseQuantity,
                    RowVersion = Convert.ToBase64String(sourceLine.RowVersion)
                }
            ],
            Targets = targets
        });

        reconciliationCalls.Should().ContainSingle(x =>
            x.Method == nameof(IInputInvoiceReconciliationService
                .InvalidateWithinTransactionAsync) && x.ReceiptId == source.Id);
        reconciliationCalls.Where(x => x.Method == nameof(
                IInputInvoiceReconciliationService.RefreshWithinTransactionAsync))
            .Select(x => x.ReceiptId).Should().Equal(
                retainExistingInvoice
                    ? [source.Id, source.Id, .. allocations.Skip(1).Select((_, index) => 201 + index)]
                    : allocations.Select((_, index) => index == 0 ? source.Id : 200 + index));

        if (retainExistingInvoice)
        {
            supplierResolutionReceiptReads.Should().Be(1);
            ownerGuardCalls.Should().Be(2);
            source.SupplierId.Should().Be(supplier.Id);
            source.Supplier.Should().BeSameAs(supplier);
            children.Should().OnlyContain(x =>
                x.SupplierId == supplier.Id && ReferenceEquals(x.Supplier, supplier));
        }

        return auditEvents;
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static Task CaptureAudit(
        ICollection<PurchaseReceiptAuditEvent> auditEvents,
        PurchaseReceiptAuditEvent auditEvent)
    {
        auditEvents.Add(auditEvent);
        return Task.CompletedTask;
    }

    private static Task<StockDocument?> ReturnSupplierResolutionReceipt(
        StockDocument source,
        ref int reads)
    {
        reads++;
        return Task.FromResult<StockDocument?>(source);
    }

    private static Task AddChild(
        StockDocument child,
        int id,
        ICollection<StockDocument> children)
    {
        child.Id = id;
        children.Add(child);
        return Task.CompletedTask;
    }

    private static T CreateThrowingProxy<T>() where T : class =>
        CreateProxy<T>((method, _) =>
            throw new InvalidOperationException($"Unexpected dependency call: {method.Name}"));

    private static T CreateProxy<T>(
        Func<MethodInfo, object?[]?, object?> handler)
        where T : class
    {
        var proxy = DispatchProxy.Create<T, DelegateProxy>();
        ((DelegateProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private static string Read(string relativePath)
        => File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GaoApp.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private class DelegateProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = default!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            Handler(targetMethod!, args);
    }

    private sealed class TenantContextStub : ITenantContext
    {
        public int? StoreId => 1;
        public bool IsHostAdmin => false;
        public string? Subdomain => "split-audit-test";
    }

    private sealed class CurrentUserStub : ICurrentUser
    {
        public int? UserId => 42;
        public string? UserName => "split-audit-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
