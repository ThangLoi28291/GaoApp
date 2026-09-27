using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Application.Services.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Invoices;
using GaoApp.Infrastructure.Repositories.Orders;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Invoices;

public sealed class InvoiceIssuanceRouteServiceTests
{
    private static readonly DateTime NowUtc =
        new(2026, 9, 26, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Initial_route_can_be_selected_for_completed_order()
    {
        await using var h = CreateHarness();

        var result =
            await h.Service.SetInitialRouteAsync(
                100,
                InvoiceIssuanceRoute.Manual);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            InvoiceIssuanceRoute.Manual,
            result.Value.Route);

        var order =
            await h.Context.Orders.SingleAsync(x => x.Id == 100);

        Assert.Equal(
            InvoiceIssuanceRoute.Manual,
            order.InvoiceIssuanceRoute);

        Assert.Equal(
            NowUtc,
            order.InvoiceIssuanceRouteSelectedAtUtc);

        Assert.Equal(
            9,
            order.InvoiceIssuanceRouteSelectedByUserId);
    }

    [Fact]
    public async Task Same_initial_route_retry_is_idempotent()
    {
        var selectedAt =
            NowUtc.AddMinutes(-5);

        await using var h = CreateHarness(
            initialRoute: InvoiceIssuanceRoute.Manual,
            selectedAtUtc: selectedAt);

        var result =
            await h.Service.SetInitialRouteAsync(
                100,
                InvoiceIssuanceRoute.Manual);

        Assert.True(result.IsSuccess);

        var order =
            await h.Context.Orders.SingleAsync(x => x.Id == 100);

        Assert.Equal(selectedAt,
            order.InvoiceIssuanceRouteSelectedAtUtc);
    }

    [Fact]
    public async Task Different_second_initial_route_is_rejected()
    {
        await using var h = CreateHarness(
            initialRoute: InvoiceIssuanceRoute.Automatic,
            selectedAtUtc: NowUtc.AddMinutes(-5));

        var result =
            await h.Service.SetInitialRouteAsync(
                100,
                InvoiceIssuanceRoute.Manual);

        Assert.False(result.IsSuccess);
        Assert.Equal("Conflict", result.Error.Code);

        var order =
            await h.Context.Orders.SingleAsync(x => x.Id == 100);

        Assert.Equal(
            InvoiceIssuanceRoute.Automatic,
            order.InvoiceIssuanceRoute);
    }

    [Fact]
    public async Task Manager_permission_is_required_for_later_route_change()
    {
        await using var h = CreateHarness(
            initialRoute: InvoiceIssuanceRoute.Automatic,
            permissionAllowed: false);

        var result =
            await h.Service.ChangeRouteAsync(
                100,
                InvoiceIssuanceRoute.Manual);

        Assert.False(result.IsSuccess);
        Assert.Equal("Failure", result.Error.Code);

        var order =
            await h.Context.Orders.SingleAsync(x => x.Id == 100);

        Assert.Equal(
            InvoiceIssuanceRoute.Automatic,
            order.InvoiceIssuanceRoute);
    }

    [Fact]
    public async Task Safe_invoice_allows_manager_to_change_route()
    {
        await using var h = CreateHarness(
            initialRoute: InvoiceIssuanceRoute.Automatic);

        var result =
            await h.Service.ChangeRouteAsync(
                100,
                InvoiceIssuanceRoute.Manual);

        Assert.True(result.IsSuccess);

        var order =
            await h.Context.Orders.SingleAsync(x => x.Id == 100);

        Assert.Equal(
            InvoiceIssuanceRoute.Manual,
            order.InvoiceIssuanceRoute);
    }

    [Fact]
    public async Task Active_auto_claim_blocks_route_change()
    {
        await using var h = CreateHarness(
            initialRoute: InvoiceIssuanceRoute.Automatic,
            activeClaim: true);

        var result =
            await h.Service.ChangeRouteAsync(
                100,
                InvoiceIssuanceRoute.Manual);

        Assert.False(result.IsSuccess);
        Assert.Equal("Conflict", result.Error.Code);

        var order =
            await h.Context.Orders.SingleAsync(x => x.Id == 100);

        Assert.Equal(
            InvoiceIssuanceRoute.Automatic,
            order.InvoiceIssuanceRoute);
    }

    [Fact]
    public async Task Issuing_sibling_blocks_route_change_for_whole_order()
    {
        await using var h = CreateHarness(
            initialRoute: InvoiceIssuanceRoute.Automatic,
            secondHeadStatus: InvoiceProviderStatus.Issuing);

        var result =
            await h.Service.ChangeRouteAsync(
                100,
                InvoiceIssuanceRoute.Manual);

        Assert.False(result.IsSuccess);
        Assert.Equal("Conflict", result.Error.Code);

        var order =
            await h.Context.Orders.SingleAsync(x => x.Id == 100);

        Assert.Equal(
            InvoiceIssuanceRoute.Automatic,
            order.InvoiceIssuanceRoute);
    }

    [Theory]
    [InlineData("claim")]
    [InlineData("succeeded")]
    [InlineData("issuing")]
    [InlineData("order-cancelled")]
    public async Task Change_route_reads_state_committed_while_waiting_for_claim_lock(string change)
    {
        var locks = 0;
        await using var h = CreateHarness(
            initialRoute: InvoiceIssuanceRoute.Automatic,
            onLock: async concurrent =>
            {
                locks++;
                if (change == "issuing")
                    (await concurrent.InvoiceHeads.SingleAsync(x => x.Id == 201)).ProviderStatus = InvoiceProviderStatus.Issuing;
                else if (change == "order-cancelled")
                    (await concurrent.Orders.SingleAsync(x => x.Id == 100)).Status = OrderStatus.Draft;
                else
                    concurrent.AutoInvoiceOperations.Add(new AutoInvoiceOperation
                    {
                        StoreId = 1, InvoiceHeadId = 200,
                        Status = change == "claim" ? AutoInvoiceOperationStatus.Processing : AutoInvoiceOperationStatus.Succeeded,
                        Sources = [new AutoInvoiceOperationSource
                        {
                            StoreId = 1, InvoiceHeadId = 200,
                            IsActive = change == "claim",
                            Status = change == "claim" ? AutoInvoiceSourceStatus.Claimed : AutoInvoiceSourceStatus.Succeeded
                        }]
                    });
                await concurrent.SaveChangesAsync();
            });

        var result = await h.Service.ChangeRouteAsync(100, InvoiceIssuanceRoute.Manual);

        Assert.False(result.IsSuccess);
        Assert.Equal("Conflict", result.Error.Code);
        Assert.Equal(1, locks);
        h.Context.ChangeTracker.Clear();
        Assert.Equal(InvoiceIssuanceRoute.Automatic,
            (await h.Context.Orders.SingleAsync(x => x.Id == 100)).InvoiceIssuanceRoute);
    }

    [Fact]
    public async Task Initial_route_refreshes_a_selection_committed_while_waiting_for_lock()
    {
        await using var h = CreateHarness(onLock: async concurrent =>
        {
            (await concurrent.Orders.SingleAsync(x => x.Id == 100)).InvoiceIssuanceRoute = InvoiceIssuanceRoute.Automatic;
            await concurrent.SaveChangesAsync();
        });

        var result = await h.Service.SetInitialRouteAsync(100, InvoiceIssuanceRoute.Manual);

        Assert.False(result.IsSuccess);
        h.Context.ChangeTracker.Clear();
        Assert.Equal(InvoiceIssuanceRoute.Automatic,
            (await h.Context.Orders.SingleAsync(x => x.Id == 100)).InvoiceIssuanceRoute);
    }

    private static Harness CreateHarness(
        InvoiceIssuanceRoute initialRoute =
            InvoiceIssuanceRoute.Unselected,
        DateTime? selectedAtUtc = null,
        bool permissionAllowed = true,
        bool activeClaim = false,
        InvoiceProviderStatus secondHeadStatus =
            InvoiceProviderStatus.LocalDraft,
        Func<InMemoryAppDbContext, Task>? onLock = null)
    {
        var tenant = new TestTenantContext();

        var currentUser = new TestCurrentUser();

        var options =
            new DbContextOptionsBuilder<InMemoryAppDbContext>()
                .UseInMemoryDatabase(
                    Guid.NewGuid().ToString())
                .Options;

        var db = new InMemoryAppDbContext(
            options,
            tenant,
            currentUser);

        db.VerifyRowVersionConfiguration();

        db.Orders.Add(
            new Order
            {
                Id = 100,
                StoreId = 1,
                POSShiftId = 1,
                Status = OrderStatus.Completed,
                CompletedAtUtc =
                    NowUtc.AddHours(-1),
                GrandTotal = 500_000m,

                InvoiceIssuanceRoute =
                    initialRoute,

                InvoiceIssuanceRouteSelectedAtUtc =
                    selectedAtUtc,

                InvoiceIssuanceRouteSelectedByUserId =
                    selectedAtUtc.HasValue
                        ? 7
                        : null
            });

        db.InvoiceHeads.AddRange(
            new InvoiceHead
            {
                Id = 200,
                StoreId = 1,
                OrderId = 100,
                LegalEntityId = 1,
                ProviderStatus =
                    InvoiceProviderStatus.LocalDraft,
                GrandTotal = 300_000m
            },
            new InvoiceHead
            {
                Id = 201,
                StoreId = 1,
                OrderId = 100,
                LegalEntityId = 2,
                ProviderStatus =
                    secondHeadStatus,
                GrandTotal = 200_000m
            });

        if (activeClaim)
        {
            db.AutoInvoiceOperations.Add(
                new AutoInvoiceOperation
                {
                    Id = 300,
                    StoreId = 1,
                    Status =
                        AutoInvoiceOperationStatus.Processing,
                    InvoiceHeadId = 200,
                    Sources =
                    [
                        new AutoInvoiceOperationSource
                        {
                            Id = 301,
                            StoreId = 1,
                            InvoiceHeadId = 200,
                            IsActive = true,
                            Status =
                                AutoInvoiceSourceStatus.Claimed
                        }
                    ]
                });
        }

        db.SaveChanges();

        var unitOfWork = new TestUnitOfWork(db);
        var stock = new InterleavingStockRepository(async () =>
        {
            Assert.True(unitOfWork.TransactionActive);
            if (onLock != null)
            {
                await using var concurrent = new InMemoryAppDbContext(options, tenant, currentUser);
                await onLock(concurrent);
            }
        });
        var service =
            new InvoiceIssuanceRouteService(
                new OrderRepository(db),
                new InvoiceRepository(db),
                new AutoInvoiceRepository(db),
                stock,
                unitOfWork,
                tenant,
                currentUser,
                new TestPermissions(permissionAllowed),
                new FixedTimeProvider(NowUtc));

        return new Harness(db, service);
    }

    private sealed class Harness(
        InMemoryAppDbContext context,
        InvoiceIssuanceRouteService service)
        : IAsyncDisposable
    {
        public InMemoryAppDbContext Context { get; } =
            context;

        public InvoiceIssuanceRouteService Service { get; } =
            service;

        public ValueTask DisposeAsync()
            => Context.DisposeAsync();
    }

    private sealed class TestUnitOfWork(
        InMemoryAppDbContext db)
        : IAppUnitOfWork
    {
        public bool TransactionActive { get; private set; }

        public Task<int> SaveChangesAsync(
            CancellationToken ct = default)
            => db.SaveChangesAsync(ct);

        public Task<IAppTransaction> BeginTransactionAsync(
            CancellationToken ct = default)
        {
            TransactionActive = true;
            return Task.FromResult<IAppTransaction>(new NoopTransaction());
        }
    }

    private sealed class InterleavingStockRepository(Func<Task> acquireLock) : IInvoiceInputStockRepository
    {
        public Task LockStoreForIssueAsync(int storeId, CancellationToken ct = default)
        {
            Assert.Equal(1, storeId);
            return acquireLock();
        }

        public Task<InvoiceInputStockAvailabilityDto> GetAvailabilityAsync(int invoiceHeadId, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class NoopTransaction
        : IAppTransaction
    {
        public Task CommitAsync(
            CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RollbackAsync(
            CancellationToken ct = default)
            => Task.CompletedTask;

        public ValueTask DisposeAsync()
            => ValueTask.CompletedTask;
    }

    private sealed class TestPermissions(bool allowed)
        : ICurrentStorePermissionService
    {
        public Task<bool> HasPermissionAsync(
            int storeId,
            int userId,
            string permissionCode,
            CancellationToken ct = default)
            => Task.FromResult(
                allowed &&
                storeId == 1 &&
                userId == 9 &&
                permissionCode ==
                    PermissionCodes.System.Invoice.Route);

        public Task<List<string>> GetPermissionsAsync(
            int storeId,
            int userId,
            CancellationToken ct = default)
            => Task.FromResult(new List<string>());
    }

    private sealed class TestTenantContext
        : ITenantContext
    {
        public int? StoreId => 1;

        public bool IsHostAdmin => false;

        public string? Subdomain => "test";
    }

    private sealed class TestCurrentUser
        : ICurrentUser
    {
        public int? UserId => 9;

        public string? UserName => "route-test";

        public int? TerminalId => null;

        public string? TerminalCode => null;

        public bool IsAuthenticated => true;
    }

    private sealed class FixedTimeProvider(
        DateTime nowUtc)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
            => new(
                DateTime.SpecifyKind(
                    nowUtc,
                    DateTimeKind.Utc));
    }
}