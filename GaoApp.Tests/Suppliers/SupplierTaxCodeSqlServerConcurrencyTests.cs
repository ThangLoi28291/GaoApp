using FluentAssertions;
using GaoApp.Application.Common.Helpers;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Repositories.Suppliers;
using GaoApp.Tests.Configuration;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Suppliers;

[Collection("R1FinalDatabasePreflight")]
public sealed class SupplierTaxCodeSqlServerConcurrencyTests
{
    [Fact]
    public void Supplier_model_should_expose_database_derived_normalized_tax_code()
        => typeof(Supplier).GetProperty("NormalizedTaxCode")
            .Should().NotBeNull();

    [Fact]
    public async Task Concurrent_same_store_writers_should_have_exactly_one_winner()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await database.CreateDatabaseAsync();
        int firstStoreId;
        int secondStoreId;
        await using (var migrationContext = database.CreateContext())
        {
            await migrationContext.Database.MigrateAsync();
            var firstStore = CreateStore("supplier-tax-a");
            var secondStore = CreateStore("supplier-tax-b");
            migrationContext.Stores.AddRange(firstStore, secondStore);
            await migrationContext.SaveChangesAsync();
            firstStoreId = firstStore.Id;
            secondStoreId = secondStore.Id;
        }

        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var firstRepository = new SupplierRepository(firstContext);
        var secondRepository = new SupplierRepository(secondContext);
        await firstRepository.AddAsync(CreateSupplier(firstStoreId, "NCC-A", "031.277-0607"));
        await secondRepository.AddAsync(CreateSupplier(firstStoreId, "NCC-B", " 031-277 0607 "));
        var normalized = TaxCodeIdentityNormalizer.Normalize("0312770607")!;

        var outcomes = await Task.WhenAll(
            firstRepository.SaveChangesWithActiveTaxCodeGuardAsync(firstStoreId, normalized, null),
            secondRepository.SaveChangesWithActiveTaxCodeGuardAsync(firstStoreId, normalized, null));

        outcomes.Count(static x => x).Should().Be(1);
        await using var verificationContext = database.CreateContext();
        (await verificationContext.Suppliers
                .IgnoreQueryFilters()
                .CountAsync(x => x.StoreId == firstStoreId &&
                                 x.NormalizedTaxCode == normalized &&
                                 !x.IsDeleted && x.IsActive))
            .Should().Be(1);

        var crossStoreRepository = new SupplierRepository(verificationContext);
        await crossStoreRepository.AddAsync(CreateSupplier(secondStoreId, "NCC-C", "0312770607"));
        (await crossStoreRepository.SaveChangesWithActiveTaxCodeGuardAsync(
                secondStoreId, normalized, null))
            .Should().BeTrue("the uniqueness boundary is StoreId");
    }

    private static Store CreateStore(string subdomain)
        => new()
        {
            Name = $"Store {subdomain}",
            SubDomain = subdomain,
            SubDomainNormalized = subdomain.ToUpperInvariant(),
            IsActive = true
        };

    private static Supplier CreateSupplier(int storeId, string code, string taxCode)
        => new()
        {
            StoreId = storeId,
            Code = code,
            Name = code,
            TaxCode = taxCode,
            IsActive = true
        };
}
