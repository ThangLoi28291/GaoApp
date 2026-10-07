using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Application.Services.Security;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Security;
using GaoApp.Infrastructure.Services.Delivery;
using GaoApp.Tests.Security;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Delivery;

[CollectionDefinition("DeliveryD04", DisableParallelization = true)]
public sealed class DeliveryD04Collection : ICollectionFixture<DeliveryD02Fixture> { }

internal sealed class DeliveryD04Case(DeliveryD02Case source) : IDisposable
{
    internal DeliveryD02Case Source => source;
    internal FullApplicationFixture Web => source.Web;
    internal FullApplicationFixture.Account Account => source.Account;
    internal FullApplicationFixture.Client Client => source.Client;
    internal int Id => source.Detail.Id;
    internal string Path(string operation = "") => "/admin/api/deliveries/" + Id + "/picking" + (operation.Length == 0 ? "" : "/" + operation);
    internal static async Task<DeliveryD04Case> CreateAsync(DeliveryD02Fixture fixture, decimal quantity = 2,
        decimal unitPrice = 20, decimal lineDiscount = 0, bool operational = true, bool secondLine = false,
        string? customerTier = null)
    {
        var source = await fixture.CaseAsync(false);
        try
        {
            await using (var db = source.Context())
            {
                var cart = await db.Orders.Include(x => x.Lines).SingleAsync(x => x.Id == source.CartId);
                var line = Assert.Single(cart.Lines);
                line.Quantity = quantity; line.BaseQuantity = quantity * line.Multiplier;
                line.UnitPrice = unitPrice; line.OriginalUnitPrice = unitPrice; line.LineDiscount = lineDiscount;
                line.LineTotal = DeliveryValues.PricedAmount(quantity, unitPrice) - lineDiscount;
                if (secondLine)
                    cart.Lines.Add(new OrderLine { StoreId = cart.StoreId, ProductId = line.ProductId, VariantId = line.VariantId,
                        ItemName = "D04 dòng thứ hai", UnitName = line.UnitName, Quantity = 1, BaseQuantity = 1,
                        Multiplier = 1, UnitPrice = 10, OriginalUnitPrice = 10, LineTotal = 10 });
                cart.Subtotal = cart.Lines.Sum(x => DeliveryValues.PricedAmount(x.Quantity, x.UnitPrice));
                cart.DiscountTotal = cart.Lines.Sum(x => x.LineDiscount);
                cart.GrandTotal = cart.Lines.Sum(x => x.LineTotal); cart.BalanceDue = cart.GrandTotal;
                if (customerTier is not null)
                {
                    var customer = new Customer { StoreId = cart.StoreId, Name = "D04 khách có giá", PriceTier = customerTier };
                    db.Customers.Add(customer); await db.SaveChangesAsync(); cart.CustomerId = customer.Id;
                }
                await db.SaveChangesAsync();
            }
            source.Detail = operational ? (await DeliveryD03Support.Create(source)).Delivery : await source.CreateAsync(Guid.NewGuid());
            return new(source);
        }
        catch { source.Dispose(); throw; }
    }

    internal async Task<DeliveryPickingDetailDto> GetAsync(FullApplicationFixture.Client? client = null)
        => (await (client ?? Client).Http.GetFromJsonAsync<DeliveryPickingDetailDto>(Path()))!;

    internal async Task<DeliveryPickingCommandAck> CommandAsync(string operation, object body,
        FullApplicationFixture.Client? client = null)
    {
        using var response = await (client ?? Client).Http.PostAsJsonAsync(Path(operation), body);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, operation + ": " + (int)response.StatusCode + " " + raw);
        return JsonSerializer.Deserialize<DeliveryPickingCommandAck>(raw, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    internal async Task ErrorAsync(string operation, object body, HttpStatusCode status,
        string? code = null, FullApplicationFixture.Client? client = null)
    {
        using var response = await (client ?? Client).Http.PostAsJsonAsync(Path(operation), body);
        Assert.Equal(status, response.StatusCode);
        if (code is not null)
        {
            var error = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(code, error.GetProperty("code").GetString());
        }
    }

    internal async Task<DeliveryPickingDetailDto> ClaimAsync(FullApplicationFixture.Client? client = null)
    {
        var detail = await GetAsync(client);
        await CommandAsync("claim", new DeliveryPickingEnvelope(Guid.NewGuid(), detail.Delivery.Version), client);
        return await GetAsync(client);
    }

    internal async Task<DeliveryPickingDetailDto> ReportAsync(string quantity, string? reason = null,
        FullApplicationFixture.Client? client = null)
    {
        var detail = await GetAsync(client);
        var line = Assert.Single(detail.Lines, x => x.IsActive && !x.IsReplacement);
        await CommandAsync("report", new DeliveryPickingReportRequest(Guid.NewGuid(), detail.Delivery.Version,
            [new(line.LineId, quantity, reason)]), client);
        return await GetAsync(client);
    }

    internal async Task<DeliveryPickingDetailDto> SubmitAsync(FullApplicationFixture.Client? client = null)
    {
        var detail = await GetAsync(client);
        await CommandAsync("submit", new DeliveryPickingEnvelope(Guid.NewGuid(), detail.Delivery.Version), client);
        return await GetAsync(client);
    }

    internal async Task<DeliveryPickingDetailDto> ApproveAsync(FullApplicationFixture.Client? client = null)
    {
        var detail = await GetAsync(client);
        await CommandAsync("approve", new DeliveryPickingApproveRequest(Guid.NewGuid(), detail.Delivery.Version,
            detail.Lines.Where(x => x.IsActive || !x.IsReplacement).Select(x => new DeliveryPickingApprovalLine(
                x.LineId, x.ReportedQuantityText ?? "0", x.IsReplacement ? x.DraftOriginalCoverageText : null)).ToArray(),
            "D04 duyệt lượng thực có", "Khách đã đồng ý"), client);
        return await GetAsync(client);
    }

    internal async Task<(FullApplicationFixture.Account Account, FullApplicationFixture.Client Client)> ActorAsync(params string[] permissions)
    {
        var account = await Web.AddAccountAsync(Account.Store with { TerminalId = Web.Stores[0].TerminalId }, permissions);
        return (account, await Web.LoginAsync(account));
    }

    internal async Task<(int VariantId, int BottleId, int PackId)> ReplacementAsync(decimal bottlePrice = 120,
        decimal packPrice = 600, decimal? wholesalePrice = null)
    {
        await using var db = Source.Context();
        var original = await db.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == Account.Store.VariantId);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var variant = new ProductVariant { StoreId = Account.Store.StoreId, ProductId = original.ProductId,
            Sku = "D04-" + suffix, Price = bottlePrice, IsActive = true };
        db.ProductVariants.Add(variant); await db.SaveChangesAsync();
        var bottle = new ProductUnitConversion { StoreId = Account.Store.StoreId, ProductVariantId = variant.Id,
            UnitId = original.Product.BaseUnitId, Factor = 1, Price = bottlePrice, WholesalePrice = wholesalePrice,
            IsBaseUnit = true, IsDefaultForSale = true };
        var pack = new ProductUnitConversion { StoreId = Account.Store.StoreId, ProductVariantId = variant.Id,
            Unit = new Unit { StoreId = Account.Store.StoreId, Code = "D04-PACK-" + suffix, Name = "Lốc D04 " + suffix },
            Factor = 6, Price = packPrice, WholesalePrice = wholesalePrice is null ? null : wholesalePrice * 6 };
        db.AddRange(bottle, pack); await db.SaveChangesAsync();
        return (variant.Id, bottle.Id, pack.Id);
    }

    internal DeliveryPickingService Service(AppDbContext db, FullApplicationFixture.Account? actor = null)
    {
        actor ??= Account;
        return new(db, new D04CurrentUser(actor.UserId, actor.Store.TerminalId),
            new CurrentStorePermissionService(new UserInStoreRepository(db)));
    }

    internal async Task<string> NonDeliveryEffectsAsync()
    {
        await using var db = Source.Context();
        return JsonSerializer.Serialize(new
        {
            Stock = await db.InventoryTransactions.CountAsync(),
            Balance = await db.InventoryBalances.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.OnHandQty, x.ReservedQty }).ToArrayAsync(),
            Reservations = await db.InventoryReservations.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.ReservedQty, x.Status }).ToArrayAsync(),
            Orders = await db.Orders.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Status, x.GrandTotal, x.PaidTotal }).ToArrayAsync(),
            Payments = await db.OrderPayments.CountAsync(), Cash = await db.POSShiftCashTransactions.CountAsync(),
            Debt = await db.Set<CustomerReceivableEntry>().CountAsync(), Deposits = await db.Set<CustomerDepositEntry>().CountAsync(),
            Rewards = await db.CustomerRewardLedgers.CountAsync(), Invoices = await db.InvoiceHeads.CountAsync(),
            Journal = await db.DeliveryJournalEntries.CountAsync(), Costs = await db.DeliveryDispatchCostFragments.CountAsync()
        });
    }

    internal static string Text(decimal quantity) => quantity.ToString("0.####", CultureInfo.InvariantCulture);
    public void Dispose() => Source.Dispose();

    private sealed class D04CurrentUser(int id, int terminal) : ICurrentUser
    {
        public int? UserId => id;
        public string? UserName => "D04 SQL actor";
        public int? TerminalId => terminal;
        public string? TerminalCode => "D04";
        public bool IsAuthenticated => true;
    }
}
