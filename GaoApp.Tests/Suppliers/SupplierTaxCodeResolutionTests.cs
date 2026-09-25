using FluentAssertions;
using FluentValidation;
using GaoApp.Application.Common.Helpers;
using GaoApp.Application.DTOs.Suppliers;
using GaoApp.Application.Interfaces.Repositories.Suppliers;
using GaoApp.Application.Services.Suppliers;
using GaoApp.Domain.Entities;

namespace GaoApp.Tests.Suppliers;

public sealed class SupplierTaxCodeResolutionTests
{
    [Theory]
    [InlineData(" 031.277-0607\t\r\n/001 ", "0312770607/001")]
    [InlineData("000-001", "000001")]
    [InlineData("đvt-01", "ĐVT01")]
    [InlineData(null, null)]
    public void Normalizer_should_implement_the_locked_product_contract(
        string? input,
        string? expected)
        => TaxCodeIdentityNormalizer.Normalize(input).Should().Be(expected);

    [Fact]
    public async Task Active_create_should_use_guard_and_return_safe_duplicate_error()
    {
        var repository = new RecordingSupplierRepository { GuardOutcome = false };
        var service = CreateService(repository);

        var result = await service.CreateAsync(
            17,
            new CreateSupplierRequest
            {
                Code = "ncc-01",
                Name = "Nhà cung cấp",
                TaxCode = " 031.277-0607 ",
                Status = true
            },
            userId: 9);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Supplier.DuplicateActiveTaxCode");
        result.Error.Message.Should().NotContain("index").And.NotContain("SQL");
        repository.LastGuard.Should().NotBeNull();
        repository.LastGuard!.Value.StoreId.Should().Be(17);
        repository.LastGuard.Value.TaxCode.Should().Be("0312770607");
        repository.LastGuard.Value.ExcludeId.Should().BeNull();
        repository.AddedSupplier!.TaxCode.Should().Be("031.277-0607");
    }

    [Fact]
    public async Task Active_tax_code_change_should_guard_with_current_supplier_excluded()
    {
        var repository = new RecordingSupplierRepository
        {
            Existing = new Supplier
            {
                Id = 41,
                StoreId = 17,
                Code = "NCC-41",
                Name = "Nhà cung cấp",
                TaxCode = "0100",
                IsActive = true
            },
            GuardOutcome = false
        };
        var service = CreateService(repository);

        var result = await service.UpdateAsync(
            17,
            new UpdateSupplierRequest
            {
                Id = 41,
                Code = "NCC-41",
                Name = "Nhà cung cấp",
                TaxCode = " 020.0 ",
                Status = true,
                RowVersion = [1]
            },
            userId: 9);

        result.Error.Code.Should().Be("Supplier.DuplicateActiveTaxCode");
        repository.LastGuard.Should().NotBeNull();
        repository.LastGuard!.Value.Should().Be((17, "0200", (int?)41));
    }

    [Fact]
    public async Task Reactivation_should_use_the_same_transactional_guard()
    {
        var repository = new RecordingSupplierRepository
        {
            Existing = new Supplier
            {
                Id = 42,
                StoreId = 17,
                Code = "NCC-42",
                Name = "Nhà cung cấp",
                TaxCode = "000-001",
                IsActive = false
            },
            GuardOutcome = false
        };
        var service = CreateService(repository);

        var result = await service.ToggleStatusAsync(17, 42, userId: 9);

        result.Error.Code.Should().Be("Supplier.DuplicateActiveTaxCode");
        repository.LastGuard.Should().NotBeNull();
        repository.LastGuard!.Value.Should().Be((17, "000001", (int?)42));
    }

    private static SupplierService CreateService(RecordingSupplierRepository repository)
        => new(repository, new InlineValidator<SupplierEditDto>());

    private sealed class RecordingSupplierRepository : ISupplierRepository
    {
        public Supplier? Existing { get; init; }
        public Supplier? AddedSupplier { get; private set; }
        public bool GuardOutcome { get; init; } = true;
        public (int StoreId, string TaxCode, int? ExcludeId)? LastGuard { get; private set; }

        public Task<(IReadOnlyList<Supplier> Items, int TotalItems)> GetPagedAsync(
            int storeId, string? search, int page, int pageSize,
            CancellationToken ct = default)
            => Task.FromResult(((IReadOnlyList<Supplier>)[], 0));

        public Task<(IReadOnlyList<Supplier> Items, int TotalItems)> GetPagedAsync(
            int storeId, string? search, bool? status, int page, int pageSize,
            CancellationToken ct = default)
            => Task.FromResult(((IReadOnlyList<Supplier>)[], 0));

        public Task<(int TotalItems, int ActiveItems, int InactiveItems)> GetSummaryAsync(
            int storeId,
            CancellationToken ct = default)
            => Task.FromResult((0, 0, 0));

        public Task<Supplier?> GetByIdAsync(
            int storeId, int id, CancellationToken ct = default)
            => Task.FromResult(Existing is { StoreId: var owner, Id: var supplierId } &&
                               owner == storeId && supplierId == id
                ? Existing
                : null);

        public Task<bool> ExistsCodeAsync(
            int storeId, string code, int? excludeId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> ExistsNameAsync(
            int storeId, string name, int? excludeId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<IReadOnlyList<Supplier>> GetActiveByNormalizedTaxCodeAsync(
            int storeId, string normalizedTaxCode, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Supplier>>([]);

        public Task<Supplier?> GetHistoricalByIdAsync(
            int storeId, int supplierId, CancellationToken ct = default)
            => Task.FromResult<Supplier?>(null);

        public Task AddAsync(Supplier entity, CancellationToken ct = default)
        {
            AddedSupplier = entity;
            entity.Id = 99;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<bool> SaveChangesWithActiveTaxCodeGuardAsync(
            int storeId, string normalizedTaxCode, int? excludeSupplierId,
            CancellationToken ct = default)
        {
            LastGuard = (storeId, normalizedTaxCode, excludeSupplierId);
            return Task.FromResult(GuardOutcome);
        }
    }
}
