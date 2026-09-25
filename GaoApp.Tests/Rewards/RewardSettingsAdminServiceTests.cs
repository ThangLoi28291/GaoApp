using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Rewards;
using GaoApp.Application.Interfaces.Repositories.Rewards;
using GaoApp.Application.Services.Rewards;
using GaoApp.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GaoApp.Tests.Rewards;

public sealed class RewardSettingsAdminServiceTests
{
    [Fact]
    public async Task Missing_settings_should_create_for_authoritative_store()
    {
        var repository =
            new FakeRewardSettingsRepository();

        var service =
            CreateService(repository);

        var result =
            await service.SaveAsync(
                7,
                new SaveRewardSettingsRequest
                {
                    MoneyPerPoint = 10_000,
                    PointsPerVoucher = 10,
                    VoucherValue = 20_000,
                    IsEnabled = true,
                    Note = "Test"
                });

        Assert.True(result.IsSuccess);
        Assert.NotNull(repository.Entity);
        Assert.Equal(7, repository.Entity!.StoreId);
        Assert.Equal(1, repository.AddCalls);
        Assert.Equal(1, repository.SaveCalls);
    }

    [Fact]
    public async Task Rate_change_without_confirmation_should_fail_without_save()
    {
        var repository =
            new FakeRewardSettingsRepository
            {
                Entity = Existing()
            };

        var service =
            CreateService(repository);

        var result =
            await service.SaveAsync(
                1,
                new SaveRewardSettingsRequest
                {
                    MoneyPerPoint = 20_000,
                    PointsPerVoucher = 10,
                    VoucherValue = 20_000,
                    IsEnabled = true,
                    RowVersion =
                        Convert.ToBase64String(
                            repository.Entity.RowVersion),
                    ConfirmRateChange = false
                });

        Assert.True(result.IsFailure);
        Assert.Equal(
            "RewardSettings.RateChangeConfirmationRequired",
            result.Error.Code);

        Assert.Equal(0, repository.SaveCalls);
        Assert.Equal(
            10_000m,
            repository.Entity!.MoneyPerPoint);
    }

    [Fact]
    public async Task Rate_change_with_confirmation_should_update_same_row()
    {
        var repository =
            new FakeRewardSettingsRepository
            {
                Entity = Existing()
            };

        var originalId =
            repository.Entity.Id;

        var service =
            CreateService(repository);

        var result =
            await service.SaveAsync(
                1,
                new SaveRewardSettingsRequest
                {
                    MoneyPerPoint = 20_000,
                    PointsPerVoucher = 20,
                    VoucherValue = 30_000,
                    IsEnabled = true,
                    Note = "Policy mới",
                    RowVersion =
                        Convert.ToBase64String(
                            repository.Entity.RowVersion),
                    ConfirmRateChange = true
                });

        Assert.True(result.IsSuccess);

        Assert.Equal(
            originalId,
            repository.Entity!.Id);

        Assert.Equal(
            20_000m,
            repository.Entity.MoneyPerPoint);

        Assert.Equal(
            20,
            repository.Entity.PointsPerVoucher);

        Assert.Equal(
            30_000m,
            repository.Entity.VoucherValue);

        Assert.Equal(
            0,
            repository.AddCalls);

        Assert.Equal(
            1,
            repository.SaveCalls);
    }

    [Fact]
    public async Task Enable_or_note_only_change_should_not_require_rate_confirmation()
    {
        var repository =
            new FakeRewardSettingsRepository
            {
                Entity = Existing()
            };

        var service =
            CreateService(repository);

        var result =
            await service.SaveAsync(
                1,
                new SaveRewardSettingsRequest
                {
                    MoneyPerPoint = 10_000,
                    PointsPerVoucher = 10,
                    VoucherValue = 20_000,
                    IsEnabled = false,
                    Note = "Tạm dừng tích điểm mới",
                    RowVersion =
                        Convert.ToBase64String(
                            repository.Entity.RowVersion),
                    ConfirmRateChange = false
                });

        Assert.True(result.IsSuccess);
        Assert.False(repository.Entity!.IsEnabled);
        Assert.Equal(1, repository.SaveCalls);
    }

    [Fact]
    public async Task Stale_row_version_should_fail_before_save()
    {
        var repository =
            new FakeRewardSettingsRepository
            {
                Entity = Existing()
            };

        var service =
            CreateService(repository);

        var result =
            await service.SaveAsync(
                1,
                new SaveRewardSettingsRequest
                {
                    MoneyPerPoint = 10_000,
                    PointsPerVoucher = 10,
                    VoucherValue = 20_000,
                    IsEnabled = true,
                    RowVersion =
                        Convert.ToBase64String(
                            new byte[] { 9 }),
                    ConfirmRateChange = false
                });

        Assert.True(result.IsFailure);

        Assert.Equal(
            "RewardSettings.ConcurrencyConflict",
            result.Error.Code);

        Assert.Equal(0, repository.SaveCalls);
    }

    [Fact]
    public async Task Category_selection_is_scoped_and_keeps_hierarchy_names()
    {
        var repository = new FakeRewardSettingsRepository { Entity = Existing() };
        repository.Categories.AddRange(new[] {
            new Category { Id = 1, StoreId = 1, Name = "Sữa" },
            new Category { Id = 2, StoreId = 1, Name = "Sữa tươi", ParentId = 1 },
            new Category { Id = 3, StoreId = 2, Name = "Cửa hàng khác" }
        });
        var service = CreateService(repository);
        var current = await service.GetAsync(1);
        Assert.Equal(2, current.Categories.Count);
        Assert.Contains(current.Categories, x => x.Name == "Sữa / Sữa tươi");
        var request = CategoryRequest(current, 1);
        var result = await service.SaveAsync(1, request);
        Assert.True(result.IsSuccess);
        Assert.False(repository.Categories[0].IsRewardEligible);
        Assert.True(repository.Categories[1].IsRewardEligible); // inherits exclusion at sale time
        Assert.True(repository.Categories[2].IsRewardEligible);

        var stale = await service.SaveAsync(1, request);
        Assert.Equal("RewardSettings.ConcurrencyConflict", stale.Error.Code);
        Assert.Equal(1, repository.SaveCalls);
    }

    [Fact]
    public async Task Foreign_category_is_rejected_and_legacy_clients_preserve_exclusions()
    {
        var repository = new FakeRewardSettingsRepository { Entity = Existing() };
        repository.Categories.Add(new Category { Id = 1, StoreId = 1, Name = "Sữa", IsRewardEligible = false });
        repository.Categories.Add(new Category { Id = 2, StoreId = 2, Name = "Sữa" });
        var service = CreateService(repository);
        var current = await service.GetAsync(1);
        var invalid = await service.SaveAsync(1, CategoryRequest(current, 2));
        Assert.True(invalid.IsFailure);
        Assert.Equal(0, repository.SaveCalls);
        Assert.False(repository.Categories[0].IsRewardEligible);
        var request = CategoryRequest(current);
        request.UpdateCategoryExclusions = false;
        Assert.True((await service.SaveAsync(1, request)).IsSuccess);
        Assert.False(repository.Categories[0].IsRewardEligible);

        current = await service.GetAsync(1);
        Assert.True((await service.SaveAsync(1, CategoryRequest(current))).IsSuccess);
        Assert.True(repository.Categories[0].IsRewardEligible); // intentionally uncheck all
    }

    private static SaveRewardSettingsRequest CategoryRequest(RewardSettingsAdminDto current, params int[] excluded)
        => new() { MoneyPerPoint = current.MoneyPerPoint, PointsPerVoucher = current.PointsPerVoucher,
            VoucherValue = current.VoucherValue, IsEnabled = current.IsEnabled, RowVersion = current.RowVersion,
            UpdateCategoryExclusions = true, CategorySelectionVersion = current.CategorySelectionVersion,
            ExcludedCategoryIds = excluded.ToList() };

    private static RewardSettingsAdminService CreateService(
        IRewardSettingsRepository repository)
    {
        return new RewardSettingsAdminService(
            repository,
            NullLogger<RewardSettingsAdminService>.Instance);
    }

    private static RewardSettings Existing()
    {
        return new RewardSettings
        {
            Id = 15,
            StoreId = 1,
            MoneyPerPoint = 10_000,
            PointsPerVoucher = 10,
            VoucherValue = 20_000,
            IsEnabled = true,
            RowVersion = new byte[] { 1 }
        };
    }

    private sealed class FakeRewardSettingsRepository
        : IRewardSettingsRepository
    {
        public RewardSettings? Entity { get; set; }
        public List<Category> Categories { get; } = new();
        public Task<List<Category>> GetCategoriesForAdminAsync(int storeId, CancellationToken ct = default)
            => Task.FromResult(Categories.Where(x => x.StoreId == storeId && !x.IsDeleted).ToList());

        public int AddCalls { get; private set; }

        public int SaveCalls { get; private set; }

        public Task<RewardSettings?> GetCurrentAsync(
            CancellationToken ct = default)
            => Task.FromResult(Entity);

        public Task<RewardSettings?> GetForAdminAsync(
            int storeId,
            CancellationToken ct = default)
            => Task.FromResult(
                Entity?.StoreId == storeId
                    ? Entity
                    : null);

        public Task AddAsync(
            RewardSettings settings,
            CancellationToken ct = default)
        {
            AddCalls++;

            Entity = settings;

            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(
            CancellationToken ct = default)
        {
            SaveCalls++;

            if (Entity is not null)
            {
                Entity.RowVersion =
                    Entity.RowVersion is { Length: > 0 }
                        ? new byte[]
                        {
                            (byte)(Entity.RowVersion[0] + 1)
                        }
                        : new byte[] { 1 };
            }

            return Task.CompletedTask;
        }
    }
}
