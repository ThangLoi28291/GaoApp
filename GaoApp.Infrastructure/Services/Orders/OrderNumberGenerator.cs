using System.Data;
using GaoApp.Application.Common;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Orders;

public sealed class OrderNumberGenerator : IOrderNumberGenerator
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;

    public OrderNumberGenerator(AppDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    public async Task<string> NextAsync(CancellationToken ct = default)
    {
        var storeId = _tenantContext.StoreId!.Value;
        var dateKey = DateTime.Now.ToString("yyyyMMdd");

        var seq = await _db.Set<OrderNumberSequence>()
            .FirstOrDefaultAsync(x => x.StoreId == storeId && x.DateKey == dateKey, ct);

        if (seq == null)
        {
            seq = new OrderNumberSequence
            {
                StoreId = storeId,
                DateKey = dateKey,
                LastNumber = 1
            };

            _db.Add(seq);
        }
        else
        {
            seq.LastNumber += 1;
        }

        await _db.SaveChangesAsync(ct);

        return $"POS-{dateKey}-{seq.LastNumber:00000}";
    }
}