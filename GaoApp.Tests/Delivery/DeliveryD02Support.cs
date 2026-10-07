using System.Net.Http.Json;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Application.Services.Security;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Security;
using GaoApp.Infrastructure.Services.Delivery;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Configuration;
using GaoApp.Tests.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GaoApp.Tests.Delivery;

[CollectionDefinition("DeliveryD02", DisableParallelization = true)]
public sealed class DeliveryD02Collection : ICollectionFixture<DeliveryD02Fixture> { }

public sealed class DeliveryD02Fixture : IAsyncLifetime
{
    internal FullApplicationFixture Web { get; private set; } = null!;
    public async Task InitializeAsync() => Web = await FullApplicationFixture.StartDeliveryAsync();
    public async Task DisposeAsync() => await Web.DisposeAsync();

    internal async Task<DeliveryD02Case> CaseAsync(bool create = true)
    {
        var store = Web.Stores[0];
        await using (var db = Web.Database.CreateTenantContext(store.StoreId))
        {
            var terminal = new POSTerminal { StoreId = store.StoreId, Code = "D02-" + Guid.NewGuid().ToString("N")[..12], Name = "D02 test terminal" };
            db.POSTerminals.Add(terminal); await db.SaveChangesAsync();
            store = store with { TerminalId = terminal.Id };
        }
        var account = await Web.AddAccountAsync(store, "*");
        var client = await Web.LoginAsync(account);
        try
        {
            await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
            var draft = await client.JsonAsync(HttpMethod.Post, "/admin/pos/draft");
            var cartId = draft.GetProperty("orderId").GetInt32();
            await client.JsonAsync(HttpMethod.Post, "/admin/pos/" + cartId + "/items?variantId=" + store.VariantId + "&qty=2");
            var result = new DeliveryD02Case(Web, account, client, cartId);
            if (create) result.Detail = await result.CreateAsync(Guid.NewGuid());
            return result;
        }
        catch { client.Dispose(); throw; }
    }
}

internal sealed class DeliveryD02Case(FullApplicationFixture web, FullApplicationFixture.Account account,
    FullApplicationFixture.Client client, int cartId) : IDisposable
{
    internal FullApplicationFixture Web => web;
    internal FullApplicationFixture.Account Account => account;
    internal FullApplicationFixture.Client Client => client;
    internal int CartId => cartId;
    internal DeliveryDetailDto Detail { get; set; } = null!;
    internal AppDbContext Context(IInterceptor? interceptor = null)
    {
        var tenant = new TenantContext(); tenant.SetStore(account.Store.StoreId, account.Store.Host);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(web.Database.ConnectionString);
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new(options.Options, tenant, new ActorUser(account.UserId, account.Store.TerminalId));
    }
    internal static DeliveryFoundationService Service(AppDbContext db, int userId, int terminalId)
        => new(db, new ActorUser(userId, terminalId), new CurrentStorePermissionService(new UserInStoreRepository(db)));
    internal DeliveryFoundationService Service(AppDbContext db) => Service(db, account.UserId, account.Store.TerminalId);
    internal async Task<DeliveryDetailDto> CreateAsync(Guid key, IInterceptor? interceptor = null)
    {
        await using var db = Context(interceptor);
        await using var tx = await db.Database.BeginTransactionAsync();
        var result = await Service(db).CreateInTransactionAsync(new(key, cartId, "Khách D02", "0901234567", "Địa chỉ D02", null));
        await tx.CommitAsync(); return result;
    }
    internal DeliveryRecipientRequest Change(Guid? key = null, string? version = null, string name = "Người nhận mới")
        => new(key ?? Guid.NewGuid(), version ?? Detail.Version, name, "0901234567", "Địa chỉ mới D02", null);
    internal async Task<DeliveryDetailDto> PostAsync(DeliveryRecipientRequest request)
    {
        using var response = await client.Http.PostAsJsonAsync("/admin/api/deliveries/" + Detail.Id + "/recipient", request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, (int)response.StatusCode + " " + body);
        return (await response.Content.ReadFromJsonAsync<DeliveryDetailDto>())!;
    }
    public void Dispose() => client.Dispose();
    private sealed class ActorUser(int id, int terminal) : ICurrentUser
    {
        public int? UserId => id;
        public string? UserName => "D02 SQL actor";
        public int? TerminalId => terminal;
        public string? TerminalCode => "D02";
        public bool IsAuthenticated => true;
    }
}

internal sealed class DeliveryHistoryFailure : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context!.ChangeTracker.Entries<GaoApp.Domain.Delivery.DeliveryRevision>().Any(x => x.State == EntityState.Added))
            throw new InvalidOperationException("D02 injected failure after aggregate SaveChanges.");
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
