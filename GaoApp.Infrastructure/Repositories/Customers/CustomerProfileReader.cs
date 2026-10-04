using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Customers;
using GaoApp.Application.Interfaces.Services.Customers;
using GaoApp.Application.Interfaces.Services.Rewards;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
namespace GaoApp.Infrastructure.Repositories.Customers;

public sealed class CustomerProfileReader(AppDbContext db, ITenantContext tenant,
    ICustomerManagementService customers, ICustomerRewardService rewards) : ICustomerProfileReader
{
    private int Store => tenant.StoreId is > 0 ? tenant.StoreId.Value : throw new InvalidOperationException("Chưa chọn cửa hàng.");
    private IQueryable<Order> Orders(int id) => db.Orders.AsNoTracking().Where(x=>x.StoreId==Store && !x.IsDeleted && x.CustomerId==id);
    private IQueryable<CustomerRewardLedger> Ledgers(int id) => db.CustomerRewardLedgers.AsNoTracking().Where(x=>x.StoreId==Store && !x.IsDeleted && x.CustomerId==id);
    private IQueryable<CustomerRewardVoucher> Vouchers(int id) => db.CustomerRewardVouchers.AsNoTracking().Where(x=>x.StoreId==Store && !x.IsDeleted && x.CustomerId==id);
    private Task<bool> Exists(int id,CancellationToken ct) => db.Customers.AnyAsync(x=>x.StoreId==Store && x.Id==id && !x.IsDeleted,ct);
    public async Task<CustomerProfileSummaryDto?> SummaryAsync(int customerId,bool canViewOrders,CancellationToken ct)
    {
        if(!await Exists(customerId,ct))return null;
        var result=new CustomerProfileSummaryDto {Customer=(await customers.GetQuickViewAsync(customerId,ct))!,CanViewOrders=canViewOrders};
        var settings=await db.RewardSettings.AsNoTracking().FirstOrDefaultAsync(x=>x.StoreId==Store,ct);
        if(settings is {MoneyPerPoint:>0,PointsPerVoucher:>0}) {
            result.Rewards=await rewards.GetSummaryAsync(customerId,ct);
            if(!settings.IsEnabled)result.RewardNotice="Chương trình đang tạm ngưng tích điểm mới. Số dư và lịch sử vẫn được giữ.";
        } else result.RewardNotice="Chưa có cấu hình quy đổi điểm hợp lệ. Lịch sử vẫn hiển thị giá trị tích lũy.";
        if(canViewOrders) {
            var orders=Orders(customerId).Where(x=>x.Status==OrderStatus.Completed || x.Status==OrderStatus.Refunded);
            result.CompletedOrders=await orders.CountAsync(ct);
            var gross=await orders.SumAsync(x=>(decimal?)x.GrandTotal,ct)??0;
            var returned=await db.SalesReturns.Where(x=>x.StoreId==Store && !x.IsDeleted && x.Status==SalesReturnStatus.Completed &&
                orders.Select(o=>o.Id).Contains(x.OrderId)).SumAsync(x=>(decimal?)(x.RefundTotal+x.DepositRestoredTotal),ct)??0;
            result.NetSales=gross-returned;
            result.LastPurchaseAtUtc=await orders.MaxAsync(x=>(DateTime?)(x.CompletedAtUtc??x.CreatedAtUtc),ct);
        }
        return result;
    }
    public async Task<CustomerProfilePageDto?> PageAsync(int customerId,CustomerProfileQuery q,bool canViewOrders,CancellationToken ct)
    {
        if(!await Exists(customerId,ct))return null;
        var settings=await db.RewardSettings.AsNoTracking().FirstOrDefaultAsync(x=>x.StoreId==Store,ct);
        decimal? perPoint=settings?.MoneyPerPoint>0 ? settings.MoneyPerPoint:null;
        if(q.Tab=="overview") {
            var recent=new List<CustomerProfileRowDto>();
            recent.AddRange(await LedgerRows(customerId,canViewOrders).OrderByDescending(x=>x.AtUtc).ThenByDescending(x=>x.Id).Take(20).ToListAsync(ct));
            recent.AddRange(await VoucherRows(customerId,canViewOrders).OrderByDescending(x=>x.AtUtc).ThenByDescending(x=>x.Id).Take(20).ToListAsync(ct));
            // Voucher usage is an activity, never another deduction from the points ledger.
            recent.AddRange(await Vouchers(customerId).Where(x=>x.UsedAtUtc!=null).OrderByDescending(x=>x.UsedAtUtc).ThenByDescending(x=>x.Id).Take(20)
                .Select(x=>new CustomerProfileRowDto {Id=x.Id,Kind="voucher-used",Title=x.VoucherCode,AtUtc=x.UsedAtUtc!.Value,
                    Amount=x.Value,VoucherId=x.Id,OrderId=canViewOrders?x.UsedOrderId:null,Description="Dùng voucher thanh toán · Không trừ thêm điểm"}).ToListAsync(ct));
            if(canViewOrders) {
                recent.AddRange(await OrderRows(customerId).OrderByDescending(x=>x.AtUtc).ThenByDescending(x=>x.Id).Take(20).ToListAsync(ct));
                recent.AddRange(await db.SalesReturns.AsNoTracking().Where(x=>x.StoreId==Store&&!x.IsDeleted&&x.Status==SalesReturnStatus.Completed&&x.Order.CustomerId==customerId)
                    .OrderByDescending(x=>x.CompletedAtUtc??x.CreatedAtUtc).ThenByDescending(x=>x.Id).Take(20)
                    .Select(x=>new CustomerProfileRowDto {Id=x.Id,Kind="return",Title=x.ReturnNumber,AtUtc=x.CompletedAtUtc??x.CreatedAtUtc,
                        OrderId=x.OrderId,Amount=x.RefundTotal+x.DepositRestoredTotal,Description=x.Reason}).ToListAsync(ct));
            }
            var result=new CustomerProfilePageDto {Items=recent.OrderByDescending(x=>x.AtUtc).ThenByDescending(x=>x.Id).Take(20).ToList(),Page=1,PageSize=20,MoneyPerPoint=perPoint};
            result.Total=result.Items.Count;SetPoints(result);await SetPayments(result.Items,ct);return result;
        }
        var rows=q.Tab switch {"orders"=>OrderRows(customerId),"points"=>LedgerRows(customerId,canViewOrders),_=>VoucherRows(customerId,canViewOrders)};
        if(q.From.HasValue){var from=DateTime.SpecifyKind(q.From.Value.Date.AddHours(-7),DateTimeKind.Utc);rows=rows.Where(x=>x.AtUtc>=from);}
        if(q.To.HasValue){var to=DateTime.SpecifyKind(q.To.Value.Date.AddDays(1).AddHours(-7),DateTimeKind.Utc);rows=rows.Where(x=>x.AtUtc<to);}
        if(q.Status.HasValue)rows=rows.Where(x=>x.Status==q.Status.Value);
        if(!string.IsNullOrWhiteSpace(q.Search)){var term=q.Search.Trim();rows=rows.Where(x=>x.Title.Contains(term)||(x.Reference!=null && x.Reference.Contains(term))||(x.Description!=null && x.Description.Contains(term)));}
        var page=new CustomerProfilePageDto {Total=await rows.CountAsync(ct),Page=Math.Clamp(q.Page,1,100000),PageSize=q.PageSize==50?50:20,MoneyPerPoint=perPoint};
        page.Page=Math.Min(page.Page,Math.Max(1,(int)Math.Ceiling(page.Total/(double)page.PageSize)));
        page.Items=await rows.OrderByDescending(x=>x.AtUtc).ThenByDescending(x=>x.Id).Skip((page.Page-1)*page.PageSize).Take(page.PageSize).ToListAsync(ct);
        SetPoints(page);await SetPayments(page.Items,ct);return page;
    }
    private async Task SetPayments(List<CustomerProfileRowDto> rows,CancellationToken ct) {
        var ids=rows.Where(x=>x.Kind=="order").Select(x=>x.Id).ToArray();if(ids.Length==0)return;
        var payments=await db.OrderPayments.AsNoTracking().Where(x=>x.StoreId==Store&&!x.IsDeleted&&x.Amount>0&&ids.Contains(x.OrderId))
            .Select(x=>new {x.OrderId,Method=(int)x.Method}).Distinct().ToListAsync(ct);
        foreach(var row in rows.Where(x=>x.Kind=="order"))row.PaymentMethods=payments.Where(x=>x.OrderId==row.Id).Select(x=>x.Method).ToList();
    }
    private static void SetPoints(CustomerProfilePageDto page) {
        if(page.MoneyPerPoint is not >0)return;
        foreach(var row in page.Items.Where(x=>x.Kind=="points" && x.BalanceAmount.HasValue)) {
            row.BalancePoints=decimal.Floor(row.BalanceAmount!.Value/page.MoneyPerPoint.Value);
            row.DeltaPoints=row.BalancePoints-decimal.Floor((row.BalanceAmount.Value-row.Amount)/page.MoneyPerPoint.Value);
        }
    }
    private IQueryable<CustomerProfileRowDto> OrderRows(int id)=>Orders(id).Select(x=>new CustomerProfileRowDto {
        Id=x.Id,Kind="order",Title=x.OrderNumber??("Đơn #"+x.Id),OrderId=x.Id,AtUtc=x.CompletedAtUtc??x.CreatedAtUtc,
        Amount=x.GrandTotal,Description=x.Note,Reference=x.OrderNumber,Status=(int)x.Status,PaymentStatus=(int)x.PaymentStatus,Credit=x.IsCreditSale,
        Employee=db.UserInStores.Where(m=>m.StoreId==Store&&!m.IsDeleted&&m.UserId==x.POSShift.OpenedByUserId).Select(m=>m.User.FullName??m.User.UserName).FirstOrDefault(),
        Terminal=x.POSShift.Terminal.Name
    });
    private IQueryable<CustomerProfileRowDto> LedgerRows(int id,bool canViewOrders) {
        var ledger=Ledgers(id);
        return ledger.Select(x=>new CustomerProfileRowDto {
        Id=x.Id,Kind="points",Title=x.Description??"Phát sinh tích lũy",Description=x.Type==CustomerRewardLedgerType.ImportOldBalance?"Chuyển từ hệ thống cũ":x.Description,
        AtUtc=x.CreatedAtUtc,Amount=x.Amount,Status=(int)x.Type,OrderId=canViewOrders?(x.OrderId??(x.SalesReturn!=null?(int?)x.SalesReturn.OrderId:null)):null,VoucherId=x.VoucherId,
        Reference=canViewOrders ? (x.Order!=null?x.Order.OrderNumber:x.Voucher!=null?x.Voucher.VoucherCode:x.ReferenceCode): (x.Voucher!=null?x.Voucher.VoucherCode:x.ReferenceCode),
        BalanceAmount=ledger.Where(l=>l.CreatedAtUtc<x.CreatedAtUtc||(l.CreatedAtUtc==x.CreatedAtUtc&&l.Id<=x.Id)).Sum(l=>(decimal?)l.Amount)??0,
        Employee=db.UserInStores.Where(m=>m.StoreId==Store&&!m.IsDeleted&&m.UserId==x.CreatedBy).Select(m=>m.User.FullName??m.User.UserName).FirstOrDefault()
    });
    }
    private IQueryable<CustomerProfileRowDto> VoucherRows(int id,bool canViewOrders)=>Vouchers(id).Select(x=>new CustomerProfileRowDto {
        Id=x.Id,Kind="voucher",Title=x.VoucherCode,AtUtc=x.IssuedAtUtc,Amount=x.Value,Status=(int)x.Status,
        VoucherId=x.Id,OrderId=canViewOrders?x.UsedOrderId:null,Description=x.Description,Reference=x.ReferenceCode
    });
}
