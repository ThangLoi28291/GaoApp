using AutoMapper;
using FluentAssertions;
using GaoApp.Application.DTOs.Brands;
using GaoApp.Application.Interfaces.Repositories.Brands;
using GaoApp.Application.Mappings.Brands;
using GaoApp.Application.Services.Brands;
using GaoApp.Application.Validators.Brands;
using GaoApp.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace GaoApp.Tests.Services;

public class BrandServiceCancellationTests
{
    [Fact]
    public async Task Create_should_propagate_request_cancellation_from_save()
    {
        using var cancellation = new CancellationTokenSource();
        var repository = new CancellingBrandRepository(cancellation);
        var service = CreateService(repository);

        Func<Task> action = async () =>
        {
            await service.CreateAsync(
                1,
                new CreateBrandRequest
                {
                    Code = "BRD-TEST",
                    Name = "Brand test",
                    Status = true
                },
                userId: 99,
                ct: cancellation.Token);
        };

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Update_should_propagate_request_cancellation_from_save()
    {
        using var cancellation = new CancellationTokenSource();
        var repository = new CancellingBrandRepository(cancellation, NewBrand());
        var service = CreateService(repository);

        Func<Task> action = async () =>
        {
            await service.UpdateAsync(
                1,
                new UpdateBrandRequest
                {
                    Id = 10,
                    Code = "BRD-TEST",
                    Name = "Brand updated",
                    Status = true,
                    RowVersion = new byte[8]
                },
                userId: 99,
                ct: cancellation.Token);
        };

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Toggle_should_propagate_request_cancellation_from_save()
    {
        using var cancellation = new CancellationTokenSource();
        var repository = new CancellingBrandRepository(cancellation, NewBrand());
        var service = CreateService(repository);

        Func<Task> action = async () =>
        {
            await service.ToggleStatusAsync(
                1,
                10,
                userId: 99,
                ct: cancellation.Token);
        };

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Delete_should_propagate_request_cancellation_from_save()
    {
        using var cancellation = new CancellationTokenSource();
        var repository = new CancellingBrandRepository(cancellation, NewBrand());
        var service = CreateService(repository);

        Func<Task> action = async () =>
        {
            await service.SoftDeleteAsync(
                1,
                10,
                userId: 99,
                ct: cancellation.Token);
        };

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    private static BrandService CreateService(IBrandRepository repository)
    {
        var mapperConfiguration = new MapperConfiguration(
            configuration => configuration.AddProfile<BrandMappingProfile>());

        return new BrandService(
            repository,
            mapperConfiguration.CreateMapper(),
            new BrandEditDtoValidator(),
            NullLogger<BrandService>.Instance);
    }

    private static Brand NewBrand()
        => new()
        {
            Id = 10,
            StoreId = 1,
            Code = "BRD-TEST",
            Name = "Brand test",
            IsActive = true,
            RowVersion = new byte[8]
        };

    private sealed class CancellingBrandRepository : IBrandRepository
    {
        private readonly CancellationTokenSource _cancellation;
        private Brand? _brand;

        public CancellingBrandRepository(
            CancellationTokenSource cancellation,
            Brand? brand = null)
        {
            _cancellation = cancellation;
            _brand = brand;
        }

        public Task<(IReadOnlyList<Brand> Items, int TotalItems)> GetPagedAsync(
            int storeId,
            string? search,
            int page,
            int pageSize,
            CancellationToken ct = default)
            => Task.FromResult<(IReadOnlyList<Brand>, int)>(
                (Array.Empty<Brand>(), 0));

        public Task<Brand?> GetByIdAsync(
            int storeId,
            int id,
            CancellationToken ct = default)
            => Task.FromResult(
                _brand is { StoreId: var entityStoreId, Id: var entityId } &&
                entityStoreId == storeId &&
                entityId == id
                    ? _brand
                    : null);

        public Task<bool> ExistsCodeAsync(
            int storeId,
            string code,
            int? excludeId,
            CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> ExistsNameAsync(
            int storeId,
            string name,
            int? excludeId,
            CancellationToken ct = default)
            => Task.FromResult(false);

        public Task AddAsync(Brand entity, CancellationToken ct = default)
        {
            entity.Id = 10;
            _brand = entity;
            return Task.CompletedTask;
        }

        public void Remove(Brand entity)
        {
        }

        public Task SaveChangesAsync(CancellationToken ct = default)
        {
            _cancellation.Cancel();
            return Task.FromCanceled(ct);
        }
    }
}
