using GaoApp.Domain.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Data;

public partial class AppDbContext
{
    public DbSet<StorefrontSettings> StorefrontSettings => Set<StorefrontSettings>();
    public DbSet<StorefrontProduct> StorefrontProducts => Set<StorefrontProduct>();
    public DbSet<StorefrontVariant> StorefrontVariants => Set<StorefrontVariant>();
    public DbSet<StorefrontSellingUnit> StorefrontSellingUnits => Set<StorefrontSellingUnit>();
    public DbSet<StorefrontShippingZone> StorefrontShippingZones => Set<StorefrontShippingZone>();
    public DbSet<StorefrontShippingRate> StorefrontShippingRates => Set<StorefrontShippingRate>();
    public DbSet<StorefrontOrder> StorefrontOrders => Set<StorefrontOrder>();
    public DbSet<StorefrontQuote> StorefrontQuotes => Set<StorefrontQuote>();
}
