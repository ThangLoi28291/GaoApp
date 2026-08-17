using System.Reflection;
using FluentAssertions;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Purchases;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Repositories.Purchases;
using GaoApp.Application.Services.Purchases;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.Purchases;

public sealed class PurchaseOrderOutstandingManagementTests
{
    [Fact]
    public async Task Close_line_closes_exact_pending_quantity_and_records_required_audit()
    {
        var order = CreateOrder(PurchaseOrderStatus.SentToSupplier, received: 4m, shortClosed: 0m);
        var context = CreateService(order);

        await context.Service.CloseOutstandingLineAsync(order.Id, 31, new()
        {
            RowVersion = Convert.ToBase64String(order.RowVersion),
            Reason = "Nhà cung cấp ngừng giao"
        });

        var line = order.Lines.Single();
        line.ReceivedQuantity.Should().Be(4m);
        line.ShortClosedQuantity.Should().Be(6m);
        line.PendingQuantity.Should().Be(0m);
        line.ReceiptStatus.Should().Be(PurchaseOrderLineReceiptStatus.ShortClosed);
        line.ShortCloseReason.Should().Be("Nhà cung cấp ngừng giao");
        line.ShortClosedByUserId.Should().Be(42);
        line.ShortClosedAtUtc.Should().NotBeNull();
        order.Status.Should().Be(PurchaseOrderStatus.ShortClosed);
        order.Actions.Should().ContainSingle(x =>
            x.ActionType == PurchaseOrderActionType.ShortClosed &&
            x.ActorUserId == 42 && x.Note!.Contains("Nhà cung cấp ngừng giao"));
        context.SaveCalls().Should().Be(1);
        context.Transaction.Commits.Should().Be(1);
    }

    [Fact]
    public async Task Close_all_closes_every_pending_line_without_changing_received_quantity()
    {
        var order = CreateOrder(PurchaseOrderStatus.PartiallyReceived, received: 4m, shortClosed: 0m);
        order.Lines.Add(new PurchaseOrderLine
        {
            Id = 32, StoreId = 1, PurchaseOrderId = order.Id, PurchaseOrder = order,
            LineNo = 2, OrderedQuantity = 5m, ReceivedQuantity = 0m,
            ConversionFactor = 1m, ProductNameSnapshot = "Bao bì", UnitNameSnapshot = "cái"
        });
        var context = CreateService(order);

        await context.Service.CloseAllOutstandingAsync(order.Id, new()
        {
            RowVersion = Convert.ToBase64String(order.RowVersion),
            Reason = "Kết thúc đợt mua"
        });

        order.Lines.Select(x => x.ReceivedQuantity).Should().Equal(4m, 0m);
        order.Lines.Select(x => x.ShortClosedQuantity).Should().Equal(6m, 5m);
        order.Lines.Should().OnlyContain(x => x.ReceiptStatus == PurchaseOrderLineReceiptStatus.ShortClosed);
        order.Status.Should().Be(PurchaseOrderStatus.ShortClosed);
    }

    [Fact]
    public async Task Close_is_blocked_when_target_has_an_active_receipt()
    {
        var order = CreateOrder(PurchaseOrderStatus.PartiallyReceived, received: 4m, shortClosed: 0m);
        var context = CreateService(order, hasActiveReceipt: true);

        var action = () => context.Service.CloseOutstandingLineAsync(order.Id, 31, new()
        {
            RowVersion = Convert.ToBase64String(order.RowVersion),
            Reason = "Không giao tiếp"
        });

        await action.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*phiếu nhập chưa hoàn tất*");
        order.Lines.Single().ShortClosedQuantity.Should().Be(0m);
        context.SaveCalls().Should().Be(0);
        context.Transaction.Commits.Should().Be(0);
    }

    [Fact]
    public async Task Reopen_preserves_received_quantity_and_restores_pending_status()
    {
        var order = CreateOrder(PurchaseOrderStatus.ShortClosed, received: 4m, shortClosed: 6m);
        order.Actions.Add(new PurchaseOrderAction
        {
            ActionType = PurchaseOrderActionType.SentToSupplier,
            FromStatus = PurchaseOrderStatus.Approved,
            ToStatus = PurchaseOrderStatus.SentToSupplier
        });
        var context = CreateService(order);

        await context.Service.ReopenOutstandingLineAsync(order.Id, 31, new()
        {
            RowVersion = Convert.ToBase64String(order.RowVersion)
        });

        var line = order.Lines.Single();
        line.ReceivedQuantity.Should().Be(4m);
        line.ShortClosedQuantity.Should().Be(0m);
        line.PendingQuantity.Should().Be(6m);
        line.ReceiptStatus.Should().Be(PurchaseOrderLineReceiptStatus.PartiallyReceived);
        line.ShortCloseReason.Should().BeNull();
        line.ShortClosedAtUtc.Should().BeNull();
        line.ShortClosedByUserId.Should().BeNull();
        order.Status.Should().Be(PurchaseOrderStatus.PartiallyReceived);
        order.Actions.Should().ContainSingle(x => x.ActionType == PurchaseOrderActionType.ShortReopened);
    }

    [Fact]
    public async Task Reopen_unreceived_short_closed_order_restores_sent_status()
    {
        var order = CreateOrder(PurchaseOrderStatus.ShortClosed, received: 0m, shortClosed: 10m);
        order.Actions.Add(new PurchaseOrderAction
        {
            ActionType = PurchaseOrderActionType.SentToSupplier,
            FromStatus = PurchaseOrderStatus.Approved,
            ToStatus = PurchaseOrderStatus.SentToSupplier
        });
        var context = CreateService(order);

        await context.Service.ReopenAllOutstandingAsync(order.Id, new()
        {
            RowVersion = Convert.ToBase64String(order.RowVersion),
            Reason = "NCC xác nhận giao lại"
        });

        order.Status.Should().Be(PurchaseOrderStatus.SentToSupplier);
        order.Lines.Single().ReceiptStatus.Should().Be(PurchaseOrderLineReceiptStatus.NotReceived);
        order.Lines.Single().PendingQuantity.Should().Be(10m);
    }

    [Fact]
    public async Task Stale_row_version_fails_after_parent_lock_and_before_mutation()
    {
        var order = CreateOrder(PurchaseOrderStatus.SentToSupplier, received: 4m, shortClosed: 0m);
        var context = CreateService(order);

        var action = () => context.Service.CloseOutstandingLineAsync(order.Id, 31, new()
        {
            RowVersion = Convert.ToBase64String([9, 9, 9]),
            Reason = "Không giao tiếp"
        });

        await action.Should().ThrowAsync<BusinessRuleException>().WithMessage("*người khác cập nhật*");
        context.LockCalls().Should().Be(1);
        context.SaveCalls().Should().Be(0);
    }

    [Fact]
    public async Task Close_requires_a_reason_before_transaction_or_mutation()
    {
        var order = CreateOrder(PurchaseOrderStatus.SentToSupplier, received: 4m, shortClosed: 0m);
        var context = CreateService(order);

        var action = () => context.Service.CloseAllOutstandingAsync(order.Id, new()
        {
            RowVersion = Convert.ToBase64String(order.RowVersion),
            Reason = " "
        });

        await action.Should().ThrowAsync<BusinessRuleException>().WithMessage("*bắt buộc phải có lý do*");
        context.LockCalls().Should().Be(0);
    }

    private static PurchaseOrder CreateOrder(
        PurchaseOrderStatus status,
        decimal received,
        decimal shortClosed)
    {
        var order = new PurchaseOrder
        {
            Id = 30, StoreId = 1, Status = status, OrderNumber = "PO-C4",
            SupplierId = 20, ExpectedWarehouseId = 10, LegalEntityId = 5,
            RowVersion = [1, 2, 3]
        };
        order.Lines.Add(new PurchaseOrderLine
        {
            Id = 31, StoreId = 1, PurchaseOrderId = order.Id, PurchaseOrder = order,
            LineNo = 1, OrderedQuantity = 10m, ReceivedQuantity = received,
            ShortClosedQuantity = shortClosed, ConversionFactor = 1m,
            ReceiptStatus = shortClosed > 0m
                ? PurchaseOrderLineReceiptStatus.ShortClosed
                : received > 0m
                    ? PurchaseOrderLineReceiptStatus.PartiallyReceived
                    : PurchaseOrderLineReceiptStatus.NotReceived,
            ShortCloseReason = shortClosed > 0m ? "Thiếu hàng" : null,
            ShortClosedAtUtc = shortClosed > 0m ? DateTime.UtcNow.AddDays(-1) : null,
            ShortClosedByUserId = shortClosed > 0m ? 7 : null,
            ProductNameSnapshot = "Gạo", UnitNameSnapshot = "bao"
        });
        return order;
    }

    private static ServiceContext CreateService(PurchaseOrder order, bool hasActiveReceipt = false)
    {
        var saveCalls = 0;
        var lockCalls = 0;
        var repository = Proxy<IPurchaseOrderRepository>((method, _) => method.Name switch
        {
            nameof(IPurchaseOrderRepository.LockForOutstandingManagementAsync) =>
                ReturnLocked(),
            nameof(IPurchaseOrderRepository.GetDetailAsync) => Task.FromResult<PurchaseOrder?>(order),
            nameof(IPurchaseOrderRepository.HasActiveReceiptLinesAsync) =>
                Task.FromResult(hasActiveReceipt),
            nameof(IPurchaseOrderRepository.SaveChangesAsync) => Save(),
            _ => throw new NotSupportedException(method.Name)
        });
        var transaction = new RecordingTransaction();
        var unitOfWork = new RecordingUnitOfWork(transaction);
        var service = new PurchaseOrderService(
            repository,
            Proxy<IDocumentNumberSequenceRepository>((method, _) => throw new NotSupportedException(method.Name)),
            new TenantStub(),
            new CurrentUserStub(),
            unitOfWork);
        return new(service, transaction, () => saveCalls, () => lockCalls);

        Task<bool> ReturnLocked()
        {
            lockCalls++;
            return Task.FromResult(true);
        }

        Task Save()
        {
            saveCalls++;
            return Task.CompletedTask;
        }
    }

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, DelegateProxy>();
        ((DelegateProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private class DelegateProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = default!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => Handler(targetMethod!, args);
    }

    private sealed class TenantStub : ITenantContext
    {
        public int? StoreId => 1;
        public bool IsHostAdmin => false;
        public string? Subdomain => "store";
    }

    private sealed class CurrentUserStub : ICurrentUser
    {
        public int? UserId => 42;
        public string? UserName => "manager";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }

    private sealed class RecordingUnitOfWork(RecordingTransaction transaction) : IAppUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
        public Task<IAppTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Task.FromResult<IAppTransaction>(transaction);
    }

    private sealed class RecordingTransaction : IAppTransaction
    {
        public int Commits { get; private set; }
        public int Rollbacks { get; private set; }
        public Task CommitAsync(CancellationToken ct = default)
        {
            Commits++;
            return Task.CompletedTask;
        }
        public Task RollbackAsync(CancellationToken ct = default)
        {
            Rollbacks++;
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed record ServiceContext(
        PurchaseOrderService Service,
        RecordingTransaction Transaction,
        Func<int> SaveCalls,
        Func<int> LockCalls);
}
