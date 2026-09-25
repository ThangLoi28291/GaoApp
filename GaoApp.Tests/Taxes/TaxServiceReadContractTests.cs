using System.Reflection;
using FluentAssertions;
using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Taxes;
using GaoApp.Application.Interfaces.Repositories.Taxes;
using GaoApp.Application.Services.Taxes;
using GaoApp.Domain.Entities;

namespace GaoApp.Tests.Taxes;

public sealed class TaxServiceReadContractTests
{
    [Fact]
    public async Task Existing_GetPagedAsync_should_map_rate_status_and_created_timestamp()
    {
        var createdAtUtc = new DateTime(2026, 9, 1, 4, 30, 0, DateTimeKind.Utc);
        var repository = new FakeTaxRepository
        {
            Items =
            [
                new Tax
                {
                    Id = 7,
                    StoreId = 1,
                    Code = "VAT10",
                    Name = "VAT 10%",
                    Rate = 10m,
                    IsActive = true,
                    CreatedAtUtc = createdAtUtc
                }
            ]
        };

        var service = new TaxService(repository);
        var page = await service.GetPagedAsync(1, null, 1, 20);
        var item = page.Items.Should().ContainSingle().Subject;

        item.Rate.Should().Be(10m);
        item.Status.Should().BeTrue();

        var createdProperty = typeof(TaxListItemDto).GetProperty("CreatedAtUtc");
        Assert.NotNull(createdProperty);
        createdProperty.GetValue(item).Should().Be(createdAtUtc);
    }

    [Fact]
    public async Task Status_GetPagedAsync_overload_should_forward_status_and_cancellation()
    {
        var repository = new FakeTaxRepository();
        var service = new TaxService(repository);
        using var cts = new CancellationTokenSource();

        var method = typeof(TaxService).GetMethod(
            nameof(TaxService.GetPagedAsync),
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types:
            [
                typeof(int),
                typeof(string),
                typeof(bool?),
                typeof(int),
                typeof(int),
                typeof(CancellationToken)
            ],
            modifiers: null);

        Assert.NotNull(method);

        var invocation = method.Invoke(service, [1, "VAT", false, 2, 10, cts.Token]);
        var task = Assert.IsAssignableFrom<Task>(invocation);
        await task;

        repository.LastStatus.Should().BeFalse();
        repository.LastCancellationToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task GetSummaryAsync_should_forward_same_store_and_cancellation()
    {
        var repository = new FakeTaxRepository
        {
            Summary = (TotalItems: 4, ActiveItems: 3, InactiveItems: 1)
        };
        var service = new TaxService(repository);
        using var cts = new CancellationTokenSource();

        var method = typeof(TaxService).GetMethod(
            "GetSummaryAsync",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: [typeof(int), typeof(CancellationToken)],
            modifiers: null);

        Assert.NotNull(method);

        var invocation = method.Invoke(service, [9, cts.Token]);
        var task = Assert.IsAssignableFrom<Task>(invocation);
        await task;

        var result = task.GetType().GetProperty("Result")?.GetValue(task);
        var summary = Assert.IsType<(int TotalItems, int ActiveItems, int InactiveItems)>(result);

        summary.Should().Be((4, 3, 1));
        repository.LastStoreId.Should().Be(9);
        repository.LastCancellationToken.Should().Be(cts.Token);
    }

    private sealed class FakeTaxRepository : ITaxRepository
    {
        public IReadOnlyList<Tax> Items { get; init; } = [];
        public (int TotalItems, int ActiveItems, int InactiveItems) Summary { get; init; }
        public int? LastStoreId { get; private set; }
        public bool? LastStatus { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<(IReadOnlyList<Tax> Items, int TotalItems)> GetPagedAsync(
            int storeId,
            string? search,
            int page,
            int pageSize,
            CancellationToken ct = default)
        {
            LastStoreId = storeId;
            LastCancellationToken = ct;
            return Task.FromResult((Items, Items.Count));
        }

        public Task<(IReadOnlyList<Tax> Items, int TotalItems)> GetPagedAsync(
            int storeId,
            string? search,
            bool? status,
            int page,
            int pageSize,
            CancellationToken ct = default)
        {
            LastStoreId = storeId;
            LastStatus = status;
            LastCancellationToken = ct;
            return Task.FromResult((Items, Items.Count));
        }

        public Task<(int TotalItems, int ActiveItems, int InactiveItems)> GetSummaryAsync(
            int storeId,
            CancellationToken ct = default)
        {
            LastStoreId = storeId;
            LastCancellationToken = ct;
            return Task.FromResult(Summary);
        }

        public Task<Tax?> GetByIdAsync(
            int storeId,
            int id,
            CancellationToken ct = default) =>
            Task.FromResult<Tax?>(null);

        public Task<bool> ExistsCodeAsync(
            int storeId,
            string code,
            int? excludeId,
            CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<bool> ExistsNameAsync(
            int storeId,
            string name,
            int? excludeId,
            CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task AddAsync(Tax entity, CancellationToken ct = default) =>
            Task.CompletedTask;

        public void Remove(Tax entity)
        {
        }

        public Task SaveChangesAsync(CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
