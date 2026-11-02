using System.Reflection;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.InvoiceNotifications;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Configurations;
using GaoApp.Infrastructure.Services.InvoiceNotifications;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.InvoiceNotifications;

// Real SQL transaction proof with a minimal test-only source model and production capture/outbox mappings.
// No runtime configuration, business database, provider client, HTTP transport or dispatcher is used.
public sealed class InvoiceNotificationTransactionSqlTests
{
    [Fact,Trait("Category","Integration"),Trait("Scope","P3DisposableSql")]
    public async Task BusinessOrderAndNotificationCommitOrRollbackInTheSameSqlTransaction()
    {
        await WithDatabase(async options=>
        {
            int customerId;
            await using(var seed=new MinimalContext(options))
            {
                await seed.Database.EnsureCreatedAsync();
                seed.Stores.Add(new Store{Name="Synthetic atomic shop",SubDomain="p3-atomic",SubDomainNormalized="P3-ATOMIC"});
                await seed.SaveChangesAsync();
                var customer=new Customer{StoreId=1,Name="Synthetic atomic customer",Phone="0398946174"};seed.Customers.Add(customer);await seed.SaveChangesAsync();customerId=customer.Id;
                await new EfInvoiceNotificationStore(seed,Enabled()).VerifyBindingAsync(new(1,customerId,"synthetic-pilot",new string('A',64),Guid.NewGuid(),Guid.NewGuid(),123,"sdk-v1:123","0398946174",7,"in_person","Synthetic atomic proof"),Guid.NewGuid(),default);
            }
            await using(var rollback=new MinimalContext(options))
            {
                await using var transaction=await rollback.Database.BeginTransactionAsync();
                var order=Order(customerId);rollback.Orders.Add(order);await rollback.SaveChangesAsync();
                await new EfInvoiceNotificationCapture(rollback,Enabled()).CaptureFinalizedOrderAsync(order,12.75m,default);
                await rollback.SaveChangesAsync();
                Assert.Equal(1,await rollback.Orders.CountAsync());Assert.Equal(1,await rollback.InvoiceNotificationOutbox.CountAsync());
                await transaction.RollbackAsync();
            }
            await using(var physical=new MinimalContext(options))
            {Assert.Equal(0,await physical.Orders.CountAsync());Assert.Equal(0,await physical.InvoiceNotificationOutbox.CountAsync());}
            int committedOrder;
            await using(var commit=new MinimalContext(options))
            {
                await using var transaction=await commit.Database.BeginTransactionAsync();
                var order=Order(customerId);commit.Orders.Add(order);await commit.SaveChangesAsync();committedOrder=order.Id;
                await new EfInvoiceNotificationCapture(commit,Enabled()).CaptureFinalizedOrderAsync(order,12.75m,default);await commit.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            await using(var physical=new MinimalContext(options))
            {
                Assert.Equal(1,await physical.Orders.CountAsync());
                var row=Assert.Single(await physical.InvoiceNotificationOutbox.AsNoTracking().ToListAsync());
                Assert.Equal(committedOrder,row.SourceId);Assert.Equal("Pending",row.State);
                var detail=await new EfInvoiceNotificationStore(physical,Enabled()).FindAsync(1,row.Id,default);
                Assert.Equal(12.75m,detail!.Snapshot.CurrentPoints);Assert.Equal(100m,detail.Snapshot.GrandTotal);
                Assert.Equal(committedOrder,detail.Snapshot.OrderId);
            }
        });
    }

    [Fact,Trait("Category","Integration"),Trait("Scope","P3DisposableSql")]
    public async Task TwoSqlContextsCannotClaimTheSameNotificationLease()
    {
        await WithDatabase(async options=>
        {
            await using(var seed=new MinimalContext(options))
            {
                await seed.Database.EnsureCreatedAsync();
                seed.Stores.Add(new Store{Name="Synthetic claim shop",SubDomain="p3-claim",SubDomainNormalized="P3-CLAIM"});await seed.SaveChangesAsync();
                var customer=new Customer{StoreId=1,Name="Synthetic customer",Phone="0398946174"};seed.Customers.Add(customer);await seed.SaveChangesAsync();
                await new EfInvoiceNotificationStore(seed,Enabled()).VerifyBindingAsync(new(1,customer.Id,"synthetic-pilot",new string('A',64),Guid.NewGuid(),Guid.NewGuid(),123,"sdk-v1:123","0398946174",7,"in_person","Synthetic claim proof"),Guid.NewGuid(),default);
                await using var transaction=await seed.Database.BeginTransactionAsync();
                var order=Order(customer.Id);seed.Orders.Add(order);await seed.SaveChangesAsync();
                await new EfInvoiceNotificationCapture(seed,Enabled()).CaptureFinalizedOrderAsync(order,null,default);await seed.SaveChangesAsync();await transaction.CommitAsync();
            }
            async Task<InvoiceNotificationWorkItem?> Claim()
            {
                await using var db=new MinimalContext(options);
                return await new EfInvoiceNotificationStore(db,Enabled()).ClaimAsync(default);
            }
            var claimed=(await Task.WhenAll(Claim(),Claim())).Where(x=>x is not null).ToArray();
            Assert.Single(claimed);
            await using var verify=new MinimalContext(options);
            var row=await verify.InvoiceNotificationOutbox.AsNoTracking().SingleAsync();Assert.Equal(claimed[0]!.LeaseToken,row.LeaseToken);Assert.Equal(1,row.Attempts);
        });
    }

    private static IOptions<InvoiceNotificationOptions> Enabled()=>Options.Create(new InvoiceNotificationOptions{Enabled=true});
    private static Order Order(int customerId)=>new(){StoreId=1,CustomerId=customerId,POSShiftId=1,OrderNumber="SYN-ATOMIC",Status=OrderStatus.Completed,PaymentStatus=PaymentStatus.Paid,CompletedAtUtc=DateTime.UtcNow,Subtotal=100m,GrandTotal=100m,PaidTotal=100m,
        Lines=[new(){StoreId=1,ItemName="Synthetic item",Quantity=1m,UnitPrice=100m,LineTotal=100m}],Payments=[new(){StoreId=1,Method=PaymentMethod.Cash,Amount=100m}]};
    private static async Task WithDatabase(Func<DbContextOptions<MinimalContext>,Task> run)
    {
        var name="GaoApp_P3Notifications_Test_"+Guid.NewGuid().ToString("N");Guard(name);
        var connection="Server=lpc:.\\SQLEXPRESS;Database="+name+";Integrated Security=true;Encrypt=true;TrustServerCertificate=true;Pooling=false;";
        var options=new DbContextOptionsBuilder<MinimalContext>().UseSqlServer(connection).Options;
        try{await run(options);}
        finally
        {
            Guard(name);
            await using var db=new MinimalContext(options);await db.Database.EnsureDeletedAsync();
        }
    }
    private static void Guard(string name){if(!System.Text.RegularExpressions.Regex.IsMatch(name,"^GaoApp_P3Notifications_Test_[a-f0-9]{32}$"))throw new InvalidOperationException("Unsafe disposable SQL database name.");}

    private sealed class MinimalContext(DbContextOptions<MinimalContext> options):AppDbContext(options,new Tenant(),new User())
    {
        protected override void OnModelCreating(ModelBuilder model)
        {
            var allowed=new[]{typeof(Store),typeof(Customer),typeof(Order),typeof(OrderLine),typeof(OrderPayment),typeof(InvoiceRecipientBinding),typeof(InvoiceNotificationOutbox),typeof(InvoiceNotificationAudit)};
            foreach(var type in allowed)
            {
                var builder=model.Entity(type);
                foreach(var property in type.GetProperties(BindingFlags.Instance|BindingFlags.Public))
                {
                    var scalar=Nullable.GetUnderlyingType(property.PropertyType)??property.PropertyType;
                    var retained=type==typeof(Order)&&property.Name is "Lines" or "Payments"||type==typeof(OrderLine)&&property.Name=="Order"||type==typeof(OrderPayment)&&property.Name=="Order";
                    if(!retained&&(!property.CanWrite||!(scalar.IsValueType||scalar==typeof(string)||scalar==typeof(byte[]))))builder.Ignore(property.Name);
                }
            }
            foreach(var type in model.Model.GetEntityTypes().Select(x=>x.ClrType).Distinct().Where(x=>!allowed.Contains(x)).ToArray())model.Ignore(type);
            model.Entity<Order>().HasMany(x=>x.Lines).WithOne(x=>x.Order).HasForeignKey(x=>x.OrderId).OnDelete(DeleteBehavior.Restrict);
            model.Entity<Order>().HasMany(x=>x.Payments).WithOne(x=>x.Order).HasForeignKey(x=>x.OrderId).OnDelete(DeleteBehavior.Restrict);
            model.Entity<Store>().HasQueryFilter(x=>!x.IsDeleted&&x.Id==CurrentStoreId);
            model.Entity<Customer>().HasQueryFilter(x=>!x.IsDeleted&&x.StoreId==CurrentStoreId);
            model.Entity<Order>().HasQueryFilter(x=>!x.IsDeleted&&x.StoreId==CurrentStoreId);
            model.Entity<OrderLine>().HasQueryFilter(x=>!x.IsDeleted&&x.StoreId==CurrentStoreId);
            model.Entity<OrderPayment>().HasQueryFilter(x=>!x.IsDeleted&&x.StoreId==CurrentStoreId);
            model.ApplyConfiguration(new InvoiceRecipientBindingConfiguration());model.ApplyConfiguration(new InvoiceNotificationOutboxConfiguration());model.ApplyConfiguration(new InvoiceNotificationAuditConfiguration());
            model.Entity<InvoiceRecipientBinding>().HasQueryFilter(x=>x.StoreId==CurrentStoreId);
            model.Entity<InvoiceNotificationOutbox>().HasQueryFilter(x=>x.StoreId==CurrentStoreId);
            model.Entity<InvoiceNotificationAudit>().HasQueryFilter(x=>x.StoreId==CurrentStoreId);
        }
    }
    private sealed class Tenant:ITenantContext{public int? StoreId=>1;public bool IsHostAdmin=>false;public string? Subdomain=>"p3-synthetic";}
    private sealed class User:ICurrentUser{public int? UserId=>7;public string? UserName=>"Synthetic operator";public int? TerminalId=>null;public string? TerminalCode=>null;public bool IsAuthenticated=>true;}
}
