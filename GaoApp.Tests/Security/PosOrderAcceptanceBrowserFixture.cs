using System.Net;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Tests.Delivery;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

// Archive cases and commands are confined to FullApplicationFixture's disposable GUID database.
internal static class PosOrderAcceptanceBrowserFixture
{
    internal sealed record ReadCase(string Kind, int OrderId);
    internal sealed record Login(string Name, string Password);
    internal sealed record Seed(IReadOnlyList<ReadCase> Cases, int WriteOrderId, int ShiftId,
        Login ViewOnly, Login ReconcileOnly, string BeforeSnapshot, decimal CashRefundBefore);

    internal static async Task<Seed> PrepareAsync(FullApplicationFixture web,
        FullApplicationFixture.StoreSeed store, FullApplicationFixture.Account account)
    {
        var cases = new List<ReadCase>();
        await using (var db = web.Database.CreateTenantContext(store.StoreId))
        {
            var variant = await db.ProductVariants.SingleAsync(x => x.Id == store.VariantId);
            var shift = new POSShift { StoreId=store.StoreId, TerminalId=store.TerminalId, WarehouseId=store.WarehouseId,
                OpenedByUserId=account.UserId, Status=POSShiftStatus.Closed, ShiftCode="UI04D-ARCHIVE" };
            var customer = new Customer { StoreId=store.StoreId, Code="UI04D-CUSTOMER",
                Name="Khách kiểm thử <img src=x onerror=bad()> & Công ty thực phẩm gia đình", Phone="0901234567" };
            Order Archive(string number, int lines, decimal unitPrice, decimal quantity=1)
            {
                var order = new Order { StoreId=store.StoreId, POSShift=shift, Customer=customer,
                    OrderNumber="UI04D-"+number, Status=OrderStatus.Completed, PaymentStatus=PaymentStatus.Paid,
                    Subtotal=lines*unitPrice*quantity, GrandTotal=lines*unitPrice*quantity,
                    PaidTotal=lines*unitPrice*quantity, CompletedAtUtc=DateTime.UtcNow,
                    Note="Ghi chú kiểm thử <script>bad()</script>\n"+new string('N',180) };
                for(var i=0;i<lines;i++) order.Lines.Add(new OrderLine { StoreId=store.StoreId,
                    ProductId=variant.ProductId, VariantId=variant.Id, ItemName=$"Hàng {i+1} · Gạo dinh dưỡng nguyên hạt <b>không phải HTML</b> · "+new string('L',95),
                    Sku=new string('S',50), Barcode="8938505974190123456789", UnitName="Ki lô gam", SellingUnitName="Ki lô gam",
                    Quantity=quantity, BaseQuantity=quantity, Multiplier=1, UnitPrice=unitPrice,
                    OriginalUnitPrice=unitPrice, LineTotal=unitPrice*quantity });
                db.Orders.Add(order); return order;
            }
            var partial=Archive("PARTIAL-CREDIT",1,2350000);
            partial.PaidTotal=650000; partial.DepositAmount=100000; partial.BalanceDue=1700000;
            partial.IsCreditSale=true; partial.CreditInitialBalance=1700000; partial.CreditDueDate=DateTime.UtcNow.AddDays(7);
            partial.PaymentStatus=PaymentStatus.PartiallyPaid;
            var retail=Archive("RETAIL",1,135000);retail.Customer=null;
            var voucher=Archive("VOUCHERS",1,200000);voucher.DiscountTotal=60000;voucher.VoucherDiscountTotal=60000;
            voucher.GrandTotal=140000;voucher.PaidTotal=140000;
            var large=Archive("LARGE",50,48000000,1.25m);
            var empty=Archive("EMPTY",0,0);empty.Status=OrderStatus.Draft;empty.PaymentStatus=PaymentStatus.Unpaid;empty.Customer=null;
            var hkd=Archive("HKD",1,100000,3);hkd.Subtotal=300000;hkd.DiscountTotal=20000;hkd.GrandTotal=280000;hkd.PaidTotal=280000;
            hkd.Lines.Single().LineDiscount=20000;hkd.Lines.Single().LineTotal=280000;
            await db.SaveChangesAsync();
            db.OrderPayments.AddRange(new OrderPayment {StoreId=store.StoreId,OrderId=partial.Id,Method=PaymentMethod.Cash,Amount=250000},
                new OrderPayment {StoreId=store.StoreId,OrderId=partial.Id,Method=PaymentMethod.BankTransfer,Amount=300000,ReferenceCode="UI04D-PARTIAL-TRANSFER"});
            foreach(var order in new[]{retail,voucher,large,hkd}) db.OrderPayments.Add(new OrderPayment {
                StoreId=store.StoreId,OrderId=order.Id,Method=PaymentMethod.Cash,Amount=order.PaidTotal });
            for(var i=1;i<=3;i++)
            {
                var v=new CustomerRewardVoucher { StoreId=store.StoreId,CustomerId=customer.Id,
                    VoucherCode=$"UI04D-{i}-"+new string('V',38),Value=20000,RequiredAmount=600000,
                    Status=CustomerRewardVoucherStatus.Used,IssuedAtUtc=DateTime.UtcNow.AddDays(-1),UsedAtUtc=DateTime.UtcNow,UsedOrderId=voucher.Id };
                db.OrderRewardVouchers.Add(new OrderRewardVoucher {StoreId=store.StoreId,OrderId=voucher.Id,Voucher=v,VoucherValue=20000});
            }
            var hkdVariant=new ProductVariant {StoreId=store.StoreId,ProductId=variant.ProductId,Sku="UI04D-HKD-VARIANT",
                ProductVariantName="Biến thể hóa đơn kiểm thử",HasInputInvoice=true};db.ProductVariants.Add(hkdVariant);
            var firstWarehouse=await db.Warehouses.SingleAsync(x=>x.Id==store.WarehouseId);
            var secondEntity=new LegalEntity {StoreId=store.StoreId,Code="UI04D-HKD-2",Name="HKD thử nghiệm "+new string('H',110),
                LegalName="Hộ kinh doanh kiểm thử",SalePriority=2};
            var secondWarehouse=new Warehouse {StoreId=store.StoreId,LegalEntity=secondEntity,Code="UI04D-KHO-2",Name="Kho thử nghiệm "+new string('K',100)};
            db.Warehouses.Add(secondWarehouse);await db.SaveChangesAsync();
            var line=hkd.Lines.Single();line.VariantId=hkdVariant.Id;
            foreach(var (warehouse,priority,qty,amount) in new[]{(firstWarehouse,1,1m,100000m),(secondWarehouse,2,2m,180000m)})
            {
                db.OrderLegalEntityAllocations.Add(new OrderLegalEntityAllocation {StoreId=store.StoreId,OrderId=hkd.Id,OrderLineId=line.Id,
                    ProductVariantId=hkdVariant.Id,WarehouseId=warehouse.Id,LegalEntityId=warehouse.LegalEntityId,SalePriority=priority,
                    Quantity=qty,BaseQuantity=qty,UnitPrice=100000,LineTotal=amount,NetAmount=amount,
                    AllocationSource=OrderLegalEntityAllocationSource.AutoBySalePriority});
                db.InvoiceHeads.Add(new InvoiceHead {StoreId=store.StoreId,OrderId=hkd.Id,LegalEntityId=warehouse.LegalEntityId,
                    BuyerType="NoInvoice",TotalQuantity=qty,SubTotal=amount,GrandTotal=amount,ProviderStatus=InvoiceProviderStatus.LocalDraft});
            }
            await db.SaveChangesAsync();
            foreach(var (kind,order) in new[]{("partial-credit",partial),("retail",retail),("vouchers",voucher),("large",large),("empty",empty),("hkd",hkd)})cases.Add(new(kind,order.Id));
        }
        using(var pending=await DeliveryBillPresentationSeed.CreateAsync(web,headerDiscounts:true))cases.Add(new("delivery-pending",pending.OrderId));
        using(var cancelled=await DeliveryBillPresentationSeed.CreateAsync(web,state:DeliveryState.Cancelled))cases.Add(new("delivery-cancelled",cancelled.OrderId));
        using var client=await web.LoginAsync(account);
        await client.JsonAsync(HttpMethod.Post,"/admin/pos/shift/open",new {openingCash=1000,warehouseId=store.WarehouseId});
        async Task<int> Sale(int quantity)
        {
            var id=(await client.JsonAsync(HttpMethod.Post,"/admin/pos/draft")).GetProperty("orderId").GetInt32();
            await client.JsonAsync(HttpMethod.Post,$"/admin/pos/{id}/items?variantId={store.VariantId}&qty={quantity}");
            await client.JsonAsync(HttpMethod.Post,$"/admin/pos/{id}/payments",new {clientRequestId=Guid.NewGuid(),method=0,amount=quantity*20});
            await client.JsonAsync(HttpMethod.Post,$"/admin/pos/{id}/finalize");return id;
        }
        var history=await Sale(6);
        foreach(var amount in new[]{10,5})await client.JsonAsync(HttpMethod.Post,"/admin/pos/returns",new {orderId=history,type=1,
            reason="Lịch sử kiểm thử UI04d",payments=new[]{new {method=0,amount}}});
        cases.Add(new("repeated-refunds",history));
        var writeOrder=await Sale(2);
        var view=await web.AddAccountAsync(store,PermissionCodes.Pos.Order.View);
        var reconcile=await web.AddAccountAsync(store,PermissionCodes.Pos.Order.View,PermissionCodes.System.LegalEntity.Reconcile);
        // Same populated HKD data must remain absent from a view-only response.
        using var viewClient=await web.LoginAsync(view);
        var hkdId=cases.Single(x=>x.Kind=="hkd").OrderId;
        if((await viewClient.Http.GetStringAsync($"/admin/pos/order-detail/{hkdId}")).Contains("data-order-legal"))
            throw new InvalidOperationException("Populated HKD data leaked to view-only account.");
        var foreign=await web.AddAccountAsync(web.Stores[1],"*");using var foreignClient=await web.LoginAsync(foreign);
        foreach(var c in cases)if((await foreignClient.Http.GetAsync($"/admin/pos/order-detail/{c.OrderId}")).StatusCode!=HttpStatusCode.BadRequest)
            throw new InvalidOperationException("Archive case crossed store boundary: "+c.Kind);
        await using var verify=web.Database.CreateTenantContext(store.StoreId);
        var currentShift=await verify.POSShifts.SingleAsync(x=>x.TerminalId==store.TerminalId&&x.Status==POSShiftStatus.Open);
        return new(cases,writeOrder,currentShift.Id,new(view.Name,view.Password),new(reconcile.Name,reconcile.Password),
            await SnapshotAsync(web,store,writeOrder),currentShift.CashRefundTotal);
    }

    private static async Task<string> SnapshotAsync(FullApplicationFixture web,FullApplicationFixture.StoreSeed store,int writeOrderId)
    {
        await using var db=web.Database.CreateTenantContext(store.StoreId);
        return JsonSerializer.Serialize(new {
            Orders=await db.Orders.AsNoTracking().Where(x=>x.Id!=writeOrderId).OrderBy(x=>x.Id).Select(x=>new{x.Id,x.RowVersion}).ToListAsync(),
            Lines=await db.OrderLines.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.RowVersion}).ToListAsync(),
            Payments=await db.OrderPayments.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.RowVersion}).ToListAsync(),
            Returns=await db.SalesReturns.AsNoTracking().Where(x=>x.OrderId!=writeOrderId).OrderBy(x=>x.Id).Select(x=>new{x.Id,x.RowVersion}).ToListAsync(),
            Vouchers=await db.CustomerRewardVouchers.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.RowVersion}).ToListAsync(),
            Allocations=await db.OrderLegalEntityAllocations.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.RowVersion}).ToListAsync(),
            Invoices=await db.InvoiceHeads.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.RowVersion}).ToListAsync(),
            Balances=await db.InventoryBalances.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.OnHandQty,x.RowVersion}).ToListAsync(),
            Menus=await db.AdminMenuItems.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.RowVersion}).ToListAsync()
        });
    }
    internal static async Task<string> VerifyAsync(FullApplicationFixture web,FullApplicationFixture.StoreSeed store,Seed seed)
    {
        if(seed.BeforeSnapshot!=await SnapshotAsync(web,store,seed.WriteOrderId))throw new InvalidOperationException("Unexpected archive/stock/payment/menu mutation.");
        await using var db=web.Database.CreateTenantContext(store.StoreId);
        var order=await db.Orders.SingleAsync(x=>x.Id==seed.WriteOrderId);
        var returns=await db.SalesReturns.Include(x=>x.Payments).Where(x=>x.OrderId==seed.WriteOrderId).ToListAsync();
        if(order.PaidTotal!=40||returns.Count!=1||returns[0].RefundTotal!=10||returns[0].Payments.Count!=1||returns[0].Payments.Single().Amount!=10
            ||(await db.POSShifts.SingleAsync(x=>x.Id==seed.ShiftId)).CashRefundTotal!=seed.CashRefundBefore+10)
            throw new InvalidOperationException("Browser refund must post exactly once for 10 and preserve paid total 40.");
        return "PASS UI-04d SQL: nine persisted read cases, populated HKD permission/store boundaries, one browser refund of 10 exactly once; archive/voucher/allocation/invoice/stock/payment/menu snapshots unchanged.";
    }
}
