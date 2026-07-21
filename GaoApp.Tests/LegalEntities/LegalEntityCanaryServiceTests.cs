using FluentAssertions;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.LegalEntities;
using GaoApp.Application.Interfaces.Repositories.LegalEntities;
using GaoApp.Application.Interfaces.Services.LegalEntities;
using GaoApp.Application.Services.LegalEntities;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.LegalEntities;

public sealed class LegalEntityCanaryServiceTests
{
    [Fact]
    public async Task SetState_Enable_ShouldRequirePreflightAndWriteActivationAudit()
    {
        var store = Store(enabled: false);
        var repository = new FakeCanaryRepository(store);
        var service = CreateService(repository, Preflight(ready: true));

        var result = await service.SetStateAsync(new SetMultiLegalEntityEnabledRequest
        {
            IsEnabled = true,
            ExpectedCurrentState = false,
            Reason = "Bắt đầu UAT canary cửa hàng 1"
        });

        result.IsSuccess.Should().BeTrue();
        result.Value.WasChanged.Should().BeTrue();
        store.IsMultiLegalEntityEnabled.Should().BeTrue();
        store.MultiLegalEntityActivatedAtUtc.Should().NotBeNull();
        repository.Events.Should().ContainSingle(x =>
            x.Action == LegalEntityActivationAction.Activate &&
            !x.PreviousIsEnabled &&
            x.NewIsEnabled &&
            x.PreflightPassed &&
            x.ChangedByUserId == 99 &&
            x.Reason == "Bắt đầu UAT canary cửa hàng 1" &&
            !string.IsNullOrWhiteSpace(x.PreflightSnapshotJson));
        repository.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task SetState_KillSwitch_ShouldClearCurrentActivationAndWriteReason()
    {
        var store = Store(enabled: true);
        var repository = new FakeCanaryRepository(store);
        repository.Events.Add(new LegalEntityActivationEvent
        {
            Id = 1,
            StoreId = 1,
            Action = LegalEntityActivationAction.Activate,
            NewIsEnabled = true,
            OccurredAtUtc = DateTime.UtcNow.AddHours(-1),
            Reason = "Initial canary"
        });
        var service = CreateService(repository, Preflight(ready: true));

        var result = await service.SetStateAsync(new SetMultiLegalEntityEnabledRequest
        {
            IsEnabled = false,
            ExpectedCurrentState = true,
            Reason = "Dừng order mới để kiểm tra lệch tồn"
        });

        result.IsSuccess.Should().BeTrue();
        store.IsMultiLegalEntityEnabled.Should().BeFalse();
        store.MultiLegalEntityActivatedAtUtc.Should().BeNull();
        repository.Events.Should().ContainSingle(x =>
            x.Action == LegalEntityActivationAction.KillSwitch &&
            x.PreviousIsEnabled &&
            !x.NewIsEnabled &&
            x.Reason == "Dừng order mới để kiểm tra lệch tồn");
    }

    [Fact]
    public async Task SetState_StaleExpectedState_ShouldRejectWithoutAudit()
    {
        var store = Store(enabled: true);
        var repository = new FakeCanaryRepository(store);
        var service = CreateService(repository, Preflight(ready: true));

        var result = await service.SetStateAsync(new SetMultiLegalEntityEnabledRequest
        {
            IsEnabled = false,
            ExpectedCurrentState = false,
            Reason = "Thử ghi đè trạng thái cũ"
        });

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Conflict");
        repository.Events.Should().BeEmpty();
        repository.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task SetState_EnableAfterHistoricalKillSwitch_ShouldWriteReactivate()
    {
        var repository = new FakeCanaryRepository(Store(enabled: false));
        repository.Events.Add(new LegalEntityActivationEvent
        {
            Id = 1,
            StoreId = 1,
            Action = LegalEntityActivationAction.KillSwitch,
            PreviousIsEnabled = true,
            NewIsEnabled = false,
            OccurredAtUtc = DateTime.UtcNow.AddMinutes(-5),
            ActivationAtUtc = DateTime.UtcNow.AddHours(-1),
            Reason = "Historical kill switch"
        });
        var service = CreateService(repository, Preflight(ready: true));

        var result = await service.SetStateAsync(new SetMultiLegalEntityEnabledRequest
        {
            IsEnabled = true,
            ExpectedCurrentState = false,
            Reason = "Bật lại sau khi kiểm tra"
        });

        result.IsSuccess.Should().BeTrue();
        repository.Events.Should().ContainSingle(x =>
            x.Action == LegalEntityActivationAction.Reactivate && x.NewIsEnabled);
    }

    [Fact]
    public async Task SetState_RowVersionConflict_ShouldReturnConflictInsteadOfFailure500()
    {
        var repository = new FakeCanaryRepository(Store(enabled: false))
        {
            TrySaveSuccess = false
        };
        var service = CreateService(repository, Preflight(ready: true));

        var result = await service.SetStateAsync(new SetMultiLegalEntityEnabledRequest
        {
            IsEnabled = true,
            ExpectedCurrentState = false,
            Reason = "Mô phỏng hai quản trị viên cùng bật"
        });

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Conflict");
        repository.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task GetStatus_AllocationMismatch_ShouldReportCriticalHealth()
    {
        var repository = new FakeCanaryRepository(Store(enabled: true))
        {
            Metrics = new LegalEntityCanaryOperationalMetricsDto { AllocatedOrderCount = 3 }
        };
        repository.AllocationMismatchCount = 1;
        var service = CreateService(repository, Preflight(ready: true));

        var result = await service.GetStatusAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.HealthStatus.Should().Be("Critical");
        result.Value.Metrics.AllocationMismatchCount.Should().Be(1);
        result.Value.HealthChecks.Should().Contain(x =>
            x.Code == "Canary.AllocationMismatch" && !x.IsPassed);
    }

    private static LegalEntityCanaryService CreateService(
        FakeCanaryRepository repository,
        LegalEntityActivationPreflightDto preflight)
        => new(
            new FakeLegalEntityService(preflight),
            repository,
            new FakeCurrentStore(),
            new FakeCurrentUser());

    private static Store Store(bool enabled)
        => new()
        {
            Id = 1,
            IsMultiLegalEntityEnabled = enabled,
            MultiLegalEntityActivatedAtUtc = enabled ? DateTime.UtcNow.AddHours(-1) : null
        };

    private static LegalEntityActivationPreflightDto Preflight(bool ready)
        => new()
        {
            IsConfigurationReady = ready,
            CanActivate = ready,
            ActiveLegalEntityCount = ready ? 2 : 1,
            Checks =
            [
                new LegalEntityPreflightCheckDto
                {
                    Code = "Test",
                    Title = "Cấu hình test",
                    IsPassed = ready,
                    Level = "Error",
                    Message = ready ? "Đạt" : "Chưa đạt"
                }
            ]
        };

    private sealed class FakeCanaryRepository : ILegalEntityCanaryRepository
    {
        public FakeCanaryRepository(Store store) => Store = store;
        public Store Store { get; }
        public List<LegalEntityActivationEvent> Events { get; } = [];
        public LegalEntityCanaryOperationalMetricsDto Metrics { get; init; } = new();
        public int AllocationMismatchCount { get; set; }
        public int InvoiceMismatchCount { get; set; }
        public bool TrySaveSuccess { get; init; } = true;
        public int SaveCount { get; private set; }

        public Task<Store?> GetStoreAsync(int storeId, bool forUpdate, CancellationToken ct = default)
            => Task.FromResult<Store?>(Store.Id == storeId ? Store : null);

        public Task<List<LegalEntityActivationEvent>> GetRecentEventsAsync(
            int storeId, int take, CancellationToken ct = default)
            => Task.FromResult(Events.OrderByDescending(x => x.OccurredAtUtc).Take(take).ToList());

        public Task<LegalEntityCanaryOperationalMetricsDto> GetOperationalMetricsAsync(
            int storeId, DateTime sinceUtc, DateTime nowUtc, CancellationToken ct = default)
            => Task.FromResult(Metrics);

        public Task<(int AllocationMismatchCount, int InvoiceMismatchCount)>
            GetReconciliationMismatchCountsAsync(
                int storeId,
                DateTime sinceUtc,
                DateTime nowUtc,
                CancellationToken ct = default)
            => Task.FromResult((AllocationMismatchCount, InvoiceMismatchCount));

        public Task AddEventAsync(LegalEntityActivationEvent activationEvent, CancellationToken ct = default)
        {
            activationEvent.Id = Events.Count + 1;
            Events.Add(activationEvent);
            return Task.CompletedTask;
        }

        public Task<bool> TrySaveChangesAsync(CancellationToken ct = default)
        {
            SaveCount++;
            return Task.FromResult(TrySaveSuccess);
        }
    }

    private sealed class FakeLegalEntityService : ILegalEntityService
    {
        private readonly LegalEntityActivationPreflightDto _preflight;
        public FakeLegalEntityService(LegalEntityActivationPreflightDto preflight) => _preflight = preflight;

        public Task<Result<LegalEntityActivationPreflightDto>> GetActivationPreflightAsync(CancellationToken ct = default)
            => Task.FromResult(Result<LegalEntityActivationPreflightDto>.Success(_preflight));

        public Task<Result<List<LegalEntityDto>>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<List<LegalEntityOptionDto>>> GetActiveOptionsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<LegalEntityManagementDto>> GetManagementAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<bool>> SetMultiLegalEntityEnabledAsync(bool isEnabled, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<int>> CreateAsync(CreateLegalEntityRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<bool>> UpdateAsync(UpdateLegalEntityRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<bool>> SetActiveAsync(int legalEntityId, bool isActive, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<bool>> SetDefaultWarehouseAsync(int legalEntityId, int warehouseId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeCurrentStore : ICurrentStore
    {
        public int StoreId => 1;
    }

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public int? UserId => 99;
        public string? UserName => "canary-admin";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
