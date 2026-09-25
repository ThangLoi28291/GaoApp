using GaoApp.Application.DTOs.StoreBankAccounts;
using GaoApp.Application.Services.StoreBankAccounts;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Repositories.StoreBankAccounts;
using GaoApp.Tests.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

public sealed class BankAccountConcurrencySqlServerTests
{
    [Fact]
    public async Task Concurrent_switches_are_atomic_tenant_scoped_and_enforced_by_sql()
    {
        await using var fixture = new InventoryPostingLocalDb();
        await fixture.MigrateAsync();
        var firstStore = await fixture.SeedInventoryCatalogAsync();
        var otherStore = await fixture.SeedInventoryCatalogAsync();
        int first, second, other;
        await using (var db = fixture.CreateTenantContext(firstStore.StoreId))
        {
            Assert.False(db.Database.HasPendingModelChanges());
            var service = new StoreBankAccountService(new StoreBankAccountRepository(db));
            first = await service.CreateAsync(Dto("111", true));
            second = await service.CreateAsync(Dto("222", false));
        }
        await using (var db = fixture.CreateTenantContext(otherStore.StoreId))
            other = await new StoreBankAccountService(new StoreBankAccountRepository(db)).CreateAsync(Dto("111", true));

        // Each request owns its own DbContext/connection. No in-process semaphore.
        await Task.WhenAll(Enumerable.Range(0, 50).Select(async i =>
        {
            await using var db = fixture.CreateTenantContext(firstStore.StoreId);
            await new StoreBankAccountService(new StoreBankAccountRepository(db)).SetDefaultAsync(i % 2 == 0 ? first : second);
        }));
        await using (var host = fixture.CreateHostContext())
        {
            Assert.Single(await host.StoreBankAccounts.Where(x => x.StoreId == firstStore.StoreId && x.IsDefault).ToListAsync());
            Assert.True((await host.StoreBankAccounts.SingleAsync(x => x.Id == other)).IsDefault);
        }

        // A failed second save rolls back the first (clearing the old default).
        await using (var db = fixture.CreateTenantContext(firstStore.StoreId))
        {
            var before = await db.StoreBankAccounts.AsNoTracking().SingleAsync(x => x.IsDefault);
            var repo = new StoreBankAccountRepository(db);
            await Assert.ThrowsAsync<InvalidOperationException>(() => repo.ExecuteStoreWriteAsync<bool>(async () =>
            { await repo.ClearDefaultAsync(); throw new InvalidOperationException("Synthetic write failure"); }));
            db.ChangeTracker.Clear();
            Assert.Equal(before.Id, (await db.StoreBankAccounts.SingleAsync(x => x.IsDefault)).Id);
        }

        // Even a writer bypassing the service cannot insert a second default.
        await using (var db = fixture.CreateTenantContext(firstStore.StoreId))
        {
            db.StoreBankAccounts.Add(new StoreBankAccount { BankCode = "TEST", BankName = "Test", AccountName = "Test", AccountNumber = "333", IsDefault = true });
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.True(ex.InnerException is SqlException { Number: 2601 or 2627 });
        }

        await using (var db = fixture.CreateTenantContext(firstStore.StoreId))
        {
            var service = new StoreBankAccountService(new StoreBankAccountRepository(db));
            var target = await service.GetByIdAsync(first); target!.IsDefault = true;
            await service.UpdateAsync(target);
            await service.ToggleStatusAsync(first);
            Assert.False((await service.GetByIdAsync(first))!.IsDefault);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetDefaultAsync(first));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetDefaultAsync(other));
        }
    }

    [Fact]
    public async Task Migration_refuses_ambiguous_existing_defaults_without_deleting_data()
    {
        await using var fixture = new InventoryPostingLocalDb();
        await fixture.MigrateAsync("20260909024821_AddAcbConfirmationAudit");
        var seed = await fixture.SeedInventoryCatalogAsync();
        await using (var db = fixture.CreateTenantContext(seed.StoreId))
        {
            db.StoreBankAccounts.AddRange(
                new StoreBankAccount { AccountNumber = "old1", IsDefault = true },
                new StoreBankAccount { AccountNumber = "old2", IsDefault = true });
            await db.SaveChangesAsync();
        }
        var ex = await Assert.ThrowsAsync<SqlException>(() => fixture.MigrateAsync());
        Assert.Equal(51001, ex.Number);
        await using (var db = fixture.CreateTenantContext(seed.StoreId))
        {
            Assert.Equal(2, await db.StoreBankAccounts.CountAsync(x => x.IsDefault));
            (await db.StoreBankAccounts.SingleAsync(x => x.AccountNumber == "old2")).IsDefault = false;
            await db.SaveChangesAsync();
        }
        await fixture.MigrateAsync();
        await using var final = fixture.CreateTenantContext(seed.StoreId);
        Assert.Equal(2, await final.StoreBankAccounts.CountAsync());
        Assert.Single(await final.StoreBankAccounts.Where(x => x.IsDefault).ToListAsync());
    }

    private static StoreBankAccountUpsertDto Dto(string number, bool isDefault) => new()
    { BankCode = "TEST", BankName = "Test bank", AccountNumber = number, AccountName = "Test store", IsActive = true, IsDefault = isDefault };
}
