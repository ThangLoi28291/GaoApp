using System.Reflection;
using FluentAssertions;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Repositories.LegalEntities;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Interfaces.Services.Products;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.Purchases;

public sealed class PurchaseReceiptRevisionWorkflowTests
{
    private static readonly byte[] CurrentVersion = [1, 2, 3, 4];

    [Fact]
    public async Task PendingApproval_physical_add_fails_before_save_or_mutation()
    {
        var document = CreateDocument(StockDocumentStatus.PendingApproval);
        var fixture = CreateFixture(document);

        Func<Task> action = () => fixture.Service.AddLineAsync(
            document.Id,
            new AddStockDocumentLineRequest
            {
                ProductVariantId = 10,
                Quantity = 1m
            });

        await action.Should().ThrowAsync<BusinessRuleException>();
        document.Lines.Should().BeEmpty();
        fixture.Repository.SaveCalls.Should().Be(0);
        fixture.AssertNoPostingCalls();
    }

    [Fact]
    public async Task PendingApproval_physical_update_fails_before_save_or_mutation()
    {
        var document = CreateDocument(StockDocumentStatus.PendingApproval);
        var line = CreateLine(document);
        var fixture = CreateFixture(document, line);
        var beforeQuantity = line.Quantity;

        Func<Task> action = () => fixture.Service.UpdateLineAsync(
            line.Id,
            new UpdateStockDocumentLineRequest { Quantity = 9m });

        await action.Should().ThrowAsync<BusinessRuleException>();
        line.Quantity.Should().Be(beforeQuantity);
        fixture.Repository.SaveCalls.Should().Be(0);
        fixture.AssertNoPostingCalls();
    }

    [Fact]
    public async Task PendingApproval_physical_delete_fails_before_repository_remove()
    {
        var document = CreateDocument(StockDocumentStatus.PendingApproval);
        var line = CreateLine(document);
        var fixture = CreateFixture(document, line);

        Func<Task> action = () => fixture.Service.DeleteLineAsync(line.Id);

        await action.Should().ThrowAsync<BusinessRuleException>();
        fixture.Repository.RemoveCalls.Should().Be(0);
        fixture.Repository.SaveCalls.Should().Be(0);
        fixture.AssertNoPostingCalls();
    }

    [Fact]
    public async Task PendingApproval_header_update_fails_before_field_change_or_save()
    {
        var document = CreateDocument(StockDocumentStatus.PendingApproval);
        var fixture = CreateFixture(document);
        var originalWarehouseId = document.WarehouseId;

        Func<Task> action = () => fixture.Service.UpdateHeaderAsync(
            ValidHeaderRequest(document.Id));

        await action.Should().ThrowAsync<BusinessRuleException>();
        document.WarehouseId.Should().Be(originalWarehouseId);
        fixture.Repository.SaveCalls.Should().Be(0);
        fixture.AssertNoPostingCalls();
    }

    [Theory]
    [InlineData(StockDocumentStatus.Draft)]
    [InlineData(StockDocumentStatus.Rejected)]
    public async Task Draft_and_returned_receipts_remain_physically_editable(
        StockDocumentStatus status)
    {
        var document = CreateDocument(status);
        var warehouse = CreateWarehouse(id: 22, legalEntityId: 33);
        var fixture = CreateFixture(document, warehouse: warehouse);

        await fixture.Service.UpdateHeaderAsync(ValidHeaderRequest(document.Id));

        document.WarehouseId.Should().Be(22);
        document.Note.Should().Be("Đã kiểm đếm lại");
        fixture.Repository.SaveCalls.Should().Be(1);
        fixture.AssertNoPostingCalls();
    }

    [Fact]
    public async Task Confirmed_receipt_remains_physically_immutable()
    {
        var document = CreateDocument(StockDocumentStatus.Confirmed);
        var fixture = CreateFixture(document);

        Func<Task> action = () => fixture.Service.UpdateHeaderAsync(
            ValidHeaderRequest(document.Id));

        await action.Should().ThrowAsync<BusinessRuleException>();
        fixture.Repository.SaveCalls.Should().Be(0);
        fixture.AssertNoPostingCalls();
    }

    [Theory]
    [InlineData(StockDocumentStatus.Draft)]
    [InlineData(StockDocumentStatus.Rejected)]
    public async Task Submit_and_resubmit_lock_physical_fields_and_clear_revision_cycle(
        StockDocumentStatus status)
    {
        var document = CreateDocument(status);
        CreateLine(document);
        document.HasRevisionRequest = true;
        document.RevisionRequestNote = "Chu kỳ cũ";
        document.RevisionRequestedAtUtc = DateTime.UtcNow.AddMinutes(-10);
        document.RevisionRequestedByUserId = 4;
        document.RevisionResolvedAtUtc = DateTime.UtcNow.AddMinutes(-5);
        document.RevisionResolvedByUserId = 5;
        var fixture = CreateFixture(document, currentUserId: 42);

        await fixture.Service.SubmitForApprovalAsync(
            document.Id,
            "Gửi duyệt lại",
            RowVersion(document));

        document.Status.Should().Be(StockDocumentStatus.PendingApproval);
        document.SubmittedByUserId.Should().Be(42);
        document.HasRevisionRequest.Should().BeFalse();
        document.RevisionRequestNote.Should().BeNull();
        document.RevisionRequestedAtUtc.Should().BeNull();
        document.RevisionRequestedByUserId.Should().BeNull();
        document.RevisionResolvedAtUtc.Should().BeNull();
        document.RevisionResolvedByUserId.Should().BeNull();
        fixture.Repository.SaveCalls.Should().Be(1);
        fixture.AssertNoPostingCalls();
    }

    [Fact]
    public async Task Revision_request_requires_PendingApproval()
    {
        var document = CreateDocument(StockDocumentStatus.Draft);
        var fixture = CreateFixture(document);

        Func<Task> action = () => fixture.Service.RequestRevisionAsync(
            document.Id,
            "Cần sửa số lượng",
            RowVersion(document));

        await action.Should().ThrowAsync<BusinessRuleException>();
        document.HasRevisionRequest.Should().BeFalse();
        fixture.Repository.SaveCalls.Should().Be(0);
    }

    [Fact]
    public async Task Revision_request_requires_a_reason()
    {
        var document = CreateDocument(StockDocumentStatus.PendingApproval);
        var fixture = CreateFixture(document);

        Func<Task> action = () => fixture.Service.RequestRevisionAsync(
            document.Id,
            "  ",
            RowVersion(document));

        await action.Should().ThrowAsync<BusinessRuleException>();
        document.HasRevisionRequest.Should().BeFalse();
        fixture.Repository.SaveCalls.Should().Be(0);
    }

    [Fact]
    public async Task Revision_request_rejects_stale_RowVersion_without_mutation()
    {
        var document = CreateDocument(StockDocumentStatus.PendingApproval);
        var fixture = CreateFixture(document);

        Func<Task> action = () => fixture.Service.RequestRevisionAsync(
            document.Id,
            "Cần sửa số lượng",
            Convert.ToBase64String([9, 9, 9]));

        await action.Should().ThrowAsync<BusinessRuleException>();
        document.HasRevisionRequest.Should().BeFalse();
        document.RevisionRequestNote.Should().BeNull();
        document.RevisionRequestedAtUtc.Should().BeNull();
        document.RevisionRequestedByUserId.Should().BeNull();
        fixture.Repository.SaveCalls.Should().Be(0);
    }

    [Fact]
    public async Task Successful_revision_request_records_flag_note_time_and_actor()
    {
        var document = CreateDocument(StockDocumentStatus.PendingApproval);
        document.RevisionResolvedAtUtc = DateTime.UtcNow.AddMinutes(-10);
        document.RevisionResolvedByUserId = 41;
        var fixture = CreateFixture(document, currentUserId: 42);

        await fixture.Service.RequestRevisionAsync(
            document.Id,
            "  Cần sửa số lượng  ",
            RowVersion(document));

        document.HasRevisionRequest.Should().BeTrue();
        document.RevisionRequestNote.Should().Be("Cần sửa số lượng");
        document.RevisionRequestedAtUtc.Should().NotBeNull();
        document.RevisionRequestedByUserId.Should().Be(42);
        document.RevisionResolvedAtUtc.Should().BeNull();
        document.RevisionResolvedByUserId.Should().BeNull();
        fixture.Repository.SaveCalls.Should().Be(1);
        fixture.AssertNoPostingCalls();
    }

    [Fact]
    public async Task Duplicate_unresolved_revision_request_fails_without_mutation()
    {
        var document = CreateDocument(StockDocumentStatus.PendingApproval);
        document.HasRevisionRequest = true;
        document.RevisionRequestNote = "Lý do ban đầu";
        var fixture = CreateFixture(document);

        Func<Task> action = () => fixture.Service.RequestRevisionAsync(
            document.Id,
            "Lý do mới",
            RowVersion(document));

        await action.Should().ThrowAsync<BusinessRuleException>();
        document.RevisionRequestNote.Should().Be("Lý do ban đầu");
        fixture.Repository.SaveCalls.Should().Be(0);
    }

    [Fact]
    public async Task Resolution_requires_an_existing_request()
    {
        var document = CreateDocument(StockDocumentStatus.PendingApproval);
        var fixture = CreateFixture(document);

        Func<Task> action = () => fixture.Service.ResolveRevisionRequestAsync(
            document.Id,
            returnToEdit: true,
            RowVersion(document));

        await action.Should().ThrowAsync<BusinessRuleException>();
        document.Status.Should().Be(StockDocumentStatus.PendingApproval);
        fixture.Repository.SaveCalls.Should().Be(0);
    }

    [Fact]
    public async Task Resolution_rejects_stale_RowVersion_without_mutation()
    {
        var document = CreateRevisionRequestedDocument();
        var fixture = CreateFixture(document);

        Func<Task> action = () => fixture.Service.ResolveRevisionRequestAsync(
            document.Id,
            returnToEdit: true,
            Convert.ToBase64String([8, 8, 8]),
            "Trả về");

        await action.Should().ThrowAsync<BusinessRuleException>();
        document.Status.Should().Be(StockDocumentStatus.PendingApproval);
        document.HasRevisionRequest.Should().BeTrue();
        document.RevisionResolvedAtUtc.Should().BeNull();
        fixture.Repository.SaveCalls.Should().Be(0);
    }

    [Fact]
    public async Task Return_to_edit_uses_employee_reason_when_manager_reason_is_absent()
    {
        var document = CreateRevisionRequestedDocument();
        var fixture = CreateFixture(document, currentUserId: 84);

        await fixture.Service.ResolveRevisionRequestAsync(
            document.Id,
            returnToEdit: true,
            RowVersion(document));

        document.Status.Should().Be(StockDocumentStatus.Rejected);
        document.HasRevisionRequest.Should().BeFalse();
        document.ApprovalNote.Should().Be("Nhập nhầm số lượng");
        document.RevisionResolvedAtUtc.Should().NotBeNull();
        document.RevisionResolvedByUserId.Should().Be(84);
        fixture.Repository.SaveCalls.Should().Be(1);
        fixture.AssertNoPostingCalls();
    }

    [Fact]
    public async Task Return_to_edit_uses_manager_reason_when_supplied()
    {
        var document = CreateRevisionRequestedDocument();
        var fixture = CreateFixture(document, currentUserId: 84);

        await fixture.Service.ResolveRevisionRequestAsync(
            document.Id,
            returnToEdit: true,
            RowVersion(document),
            "  Kiểm tra lại đơn vị nhận  ");

        document.Status.Should().Be(StockDocumentStatus.Rejected);
        document.ApprovalNote.Should().Be("Kiểm tra lại đơn vị nhận");
        document.RevisionResolvedByUserId.Should().Be(84);
        fixture.Repository.SaveCalls.Should().Be(1);
        fixture.AssertNoPostingCalls();
    }

    [Fact]
    public async Task Dismiss_keeps_PendingApproval_and_records_resolution_only()
    {
        var document = CreateRevisionRequestedDocument();
        document.ApprovalNote = "Ghi chú thương mại hiện tại";
        var fixture = CreateFixture(document, currentUserId: 85);

        await fixture.Service.ResolveRevisionRequestAsync(
            document.Id,
            returnToEdit: false,
            RowVersion(document),
            "Không dùng ghi chú này để đổi dữ liệu thương mại");

        document.Status.Should().Be(StockDocumentStatus.PendingApproval);
        document.HasRevisionRequest.Should().BeFalse();
        document.ApprovalNote.Should().Be("Ghi chú thương mại hiện tại");
        document.RevisionResolvedAtUtc.Should().NotBeNull();
        document.RevisionResolvedByUserId.Should().Be(85);
        fixture.Repository.SaveCalls.Should().Be(1);
        fixture.AssertNoPostingCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-base64")]
    public async Task Direct_manager_return_requires_a_valid_RowVersion(string? rowVersion)
    {
        var document = CreateDocument(StockDocumentStatus.PendingApproval);
        var fixture = CreateFixture(document);

        Func<Task> action = () => fixture.Service.RejectAsync(
            document.Id,
            "Cần kiểm đếm lại",
            rowVersion);

        await action.Should().ThrowAsync<BusinessRuleException>();
        document.Status.Should().Be(StockDocumentStatus.PendingApproval);
        fixture.Repository.SaveCalls.Should().Be(0);
    }

    [Fact]
    public async Task Direct_manager_return_requires_a_reason()
    {
        var document = CreateDocument(StockDocumentStatus.PendingApproval);
        var fixture = CreateFixture(document);

        Func<Task> action = () => fixture.Service.RejectAsync(
            document.Id,
            "  ",
            RowVersion(document));

        await action.Should().ThrowAsync<BusinessRuleException>();
        document.Status.Should().Be(StockDocumentStatus.PendingApproval);
        fixture.Repository.SaveCalls.Should().Be(0);
    }

    [Fact]
    public async Task Direct_manager_return_records_reason_actor_and_time_without_posting()
    {
        var document = CreateDocument(StockDocumentStatus.PendingApproval);
        var fixture = CreateFixture(document, currentUserId: 86);

        await fixture.Service.RejectAsync(
            document.Id,
            "  Kiểm đếm lại hàng thực nhận  ",
            RowVersion(document));

        document.Status.Should().Be(StockDocumentStatus.Rejected);
        document.ApprovalNote.Should().Be("Kiểm đếm lại hàng thực nhận");
        document.RevisionResolvedAtUtc.Should().NotBeNull();
        document.RevisionResolvedByUserId.Should().Be(86);
        fixture.Repository.SaveCalls.Should().Be(1);
        fixture.AssertNoPostingCalls();
    }

    private static ServiceFixture CreateFixture(
        StockDocument document,
        StockDocumentLine? line = null,
        Warehouse? warehouse = null,
        int currentUserId = 7)
    {
        var repository = new RecordingStockDocumentRepository
        {
            Document = document,
            Line = line
        };
        var postingCalls = new CallCounter();

        var service = new StockDocumentService(
            repository,
            Unused<ILegalEntityRepository>(),
            warehouse is null
                ? Unused<IWarehouseRepository>()
                : CreateProxy<IWarehouseRepository>((method, _) =>
                    method.Name == nameof(IWarehouseRepository.GetByIdAsync)
                        ? Task.FromResult<Warehouse?>(warehouse)
                        : throw new NotSupportedException(method.Name)),
            Unused<IBarcodeLookupService>(),
            Unused<IInventoryUnitResolver>(),
            Counted<IInventoryMovementService>(postingCalls),
            Counted<IInventoryMovementFactory>(postingCalls),
            Counted<IInventoryRevaluationService>(postingCalls),
            Unused<IDocumentNumberSequenceRepository>(),
            new TenantContextStub(),
            Counted<IInventoryValuationEntryRepository>(postingCalls),
            new CurrentUserStub(currentUserId));

        return new ServiceFixture(service, repository, postingCalls);
    }

    private static StockDocument CreateDocument(StockDocumentStatus status)
        => new()
        {
            Id = 11,
            StoreId = 1,
            DocumentNo = "NK-TEST-001",
            Status = status,
            Type = StockDocumentType.Receipt,
            ReceiptSource = PurchaseReceiptSource.Direct,
            DirectReceiptReason = "Khác",
            WarehouseId = 10,
            RowVersion = CurrentVersion.ToArray()
        };

    private static StockDocument CreateRevisionRequestedDocument()
    {
        var document = CreateDocument(StockDocumentStatus.PendingApproval);
        document.HasRevisionRequest = true;
        document.RevisionRequestNote = "Nhập nhầm số lượng";
        document.RevisionRequestedAtUtc = DateTime.UtcNow.AddMinutes(-5);
        document.RevisionRequestedByUserId = 7;
        return document;
    }

    private static StockDocumentLine CreateLine(StockDocument document)
    {
        var line = new StockDocumentLine
        {
            Id = 21,
            StockDocumentId = document.Id,
            StockDocument = document,
            ProductVariantId = 31,
            ProductNameSnapshot = "Gạo",
            Quantity = 2m,
            BaseQuantity = 2m,
            Factor = 1m
        };
        document.Lines.Add(line);
        return line;
    }

    private static Warehouse CreateWarehouse(int id, int legalEntityId)
    {
        var legalEntity = new LegalEntity
        {
            Id = legalEntityId,
            StoreId = 1,
            Code = "LE-01",
            Name = "HKD 01",
            IsActive = true
        };
        return new Warehouse
        {
            Id = id,
            StoreId = 1,
            Code = "WH-01",
            Name = "Kho 01",
            IsActive = true,
            LegalEntityId = legalEntityId,
            LegalEntity = legalEntity
        };
    }

    private static UpdateStockDocumentHeaderRequest ValidHeaderRequest(int documentId)
        => new()
        {
            StockDocumentId = documentId,
            LegalEntityId = 33,
            WarehouseId = 22,
            DocumentDate = new DateTime(2026, 8, 13),
            Note = "  Đã kiểm đếm lại  "
        };

    private static string RowVersion(StockDocument document)
        => Convert.ToBase64String(document.RowVersion);

    private static T Unused<T>() where T : class
        => CreateProxy<T>((method, _) => throw new NotSupportedException(method.Name));

    private static T Counted<T>(CallCounter counter) where T : class
        => CreateProxy<T>((method, _) =>
        {
            counter.Count++;
            throw new InvalidOperationException($"Unexpected posting call: {method.Name}");
        });

    private static T CreateProxy<T>(
        Func<MethodInfo, object?[]?, object?> handler)
        where T : class
    {
        var proxy = DispatchProxy.Create<T, DelegateProxy>();
        ((DelegateProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private sealed record ServiceFixture(
        StockDocumentService Service,
        RecordingStockDocumentRepository Repository,
        CallCounter PostingCalls)
    {
        public void AssertNoPostingCalls()
        {
            PostingCalls.Count.Should().Be(0);
            Repository.BeginTransactionCalls.Should().Be(0);
            Repository.CommitTransactionCalls.Should().Be(0);
            Repository.RollbackTransactionCalls.Should().Be(0);
            Repository.AddPayableCalls.Should().Be(0);
        }
    }

    private sealed class CallCounter
    {
        public int Count { get; set; }
    }

    private class DelegateProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = default!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => Handler(targetMethod!, args);
    }

    private sealed class CurrentUserStub(int userId) : ICurrentUser
    {
        public int? UserId => userId;
        public string? UserName => "workflow-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }

    private sealed class TenantContextStub : ITenantContext
    {
        public int? StoreId => 1;
        public bool IsHostAdmin => false;
        public string? Subdomain => "test";
    }

    private sealed class RecordingStockDocumentRepository : IStockDocumentRepository
    {
        public StockDocument Document { get; init; } = default!;
        public StockDocumentLine? Line { get; init; }
        public int SaveCalls { get; private set; }
        public int RemoveCalls { get; private set; }
        public int BeginTransactionCalls { get; private set; }
        public int CommitTransactionCalls { get; private set; }
        public int RollbackTransactionCalls { get; private set; }
        public int AddPayableCalls { get; private set; }

        public Task AddAsync(StockDocument entity, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<StockDocument?> GetByIdAsync(int id, CancellationToken ct = default)
            => Task.FromResult<StockDocument?>(Document);
        public Task<StockDocument?> GetDetailAsync(int id, CancellationToken ct = default)
            => Task.FromResult<StockDocument?>(Document);
        public Task<StockDocument?> GetForConfirmAsync(int id, CancellationToken ct = default)
            => Task.FromResult<StockDocument?>(Document);
        public Task<StockDocumentLine?> GetLineByIdAsync(int lineId, CancellationToken ct = default)
            => Task.FromResult(Line);
        public Task<int> GetNextLineNoAsync(int stockDocumentId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<bool> WarehouseExistsAsync(int warehouseId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<bool> SupplierExistsAsync(int supplierId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<Supplier?> GetSupplierAsync(int supplierId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ProductVariant?> GetVariantForStockDocumentAsync(int productVariantId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ProductUnitConversion?> GetConversionAsync(int productVariantId, int unitId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ProductUnitConversion?> GetBaseConversionAsync(int productVariantId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<Tax?> GetTaxAsync(int taxId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<List<Tax>> GetTaxesAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<Dictionary<int, decimal>> GetLastPurchaseBaseUnitPricesBeforeVatAsync(
            IEnumerable<int> productVariantIds,
            CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<PurchaseOrder?> GetPurchaseOrderForReceiptAsync(int purchaseOrderId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task AddPurchasePayableAsync(PurchasePayable payable, CancellationToken ct = default)
        {
            AddPayableCalls++;
            return Task.CompletedTask;
        }
        public Task<bool> PurchasePayableExistsAsync(string sourceKey, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<InventoryBalance?> GetInventoryBalanceAsync(int warehouseId, int productVariantId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task AddInventoryBalanceAsync(InventoryBalance entity, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task AddInventoryTransactionAsync(InventoryTransaction entity, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<List<StockDocument>> GetReceiptListAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task RemoveLineAsync(StockDocumentLine line, CancellationToken ct = default)
        {
            RemoveCalls++;
            return Task.CompletedTask;
        }
        public Task<bool> ExistsInventoryTransactionByReferenceLineAsync(
            InventoryReferenceType referenceType,
            string referenceId,
            int referenceLineId,
            CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task BeginTransactionAsync(CancellationToken ct = default)
        {
            BeginTransactionCalls++;
            return Task.CompletedTask;
        }
        public Task CommitTransactionAsync(CancellationToken ct = default)
        {
            CommitTransactionCalls++;
            return Task.CompletedTask;
        }
        public Task RollbackTransactionAsync(CancellationToken ct = default)
        {
            RollbackTransactionCalls++;
            return Task.CompletedTask;
        }
        public Task SaveChangesAsync(CancellationToken ct = default)
        {
            SaveCalls++;
            return Task.CompletedTask;
        }
        public Task MarkVariantsHasInputInvoiceAsync(
            IEnumerable<int> productVariantIds,
            int? userId,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
