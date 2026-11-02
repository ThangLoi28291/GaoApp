using GaoApp.Application.Common;
using GaoApp.Application.Common.Security;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Services.Assistant;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services.CustomerService;

public sealed class CustomerServiceAccess(AppDbContext db,ICurrentUser current,ITenantContext tenant)
{
    public async Task<(int Id,string Name)> Store(CancellationToken ct) {
        if(tenant.StoreId is not >0)throw new AssistantException(404,"customer_store_not_found","Không tìm thấy cửa hàng.");
        var row=await db.Stores.IgnoreQueryFilters().AsNoTracking().Where(s=>s.Id==tenant.StoreId&&s.IsActive&&!s.IsDeleted).Select(s=>new{s.Id,s.Name}).SingleOrDefaultAsync(ct);
        return row is null?throw new AssistantException(404,"customer_store_not_found","Không tìm thấy cửa hàng."):(row.Id,row.Name);
    }
    public async Task<CustomerStaff> Staff(string required,CancellationToken ct,bool invoiceNotificationVerifier=false) {
        var store=await Store(ct);if(!current.IsAuthenticated||current.UserId is not >0)throw AssistantException.Denied();
        var user=await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u=>u.Id==current.UserId&&u.IsActive&&!u.IsDeleted).Select(u=>new{u.Id,u.IsHostAdmin,u.FullName,u.UserName}).SingleOrDefaultAsync(ct)??throw AssistantException.Denied();
        var rights=user.IsHostAdmin?new[]{CustomerServiceRights.View,CustomerServiceRights.Reply,CustomerServiceRights.Manage,CustomerServiceRights.Verify,PermissionCodes.Catalog.Customer.View,PermissionCodes.Pos.Order.View}:
            await db.UserInStores.IgnoreQueryFilters().Where(m=>m.StoreId==store.Id&&m.UserId==user.Id&&m.IsActive&&!m.IsDeleted&&m.Role.StoreId==store.Id&&!m.Role.IsDeleted)
                .SelectMany(m=>m.Role.RolePermissions.Select(p=>p.Permission.Code)).Distinct().ToArrayAsync(ct);
        if(!rights.Contains(CustomerServiceRights.View)||!rights.Contains(required))throw AssistantException.Denied();
        var name=new string((user.FullName??user.UserName??"Nhân viên").Where(c=>!char.IsControl(c)).Take(80).ToArray());
        var canVerify=(rights.Contains(CustomerServiceRights.Verify)||(invoiceNotificationVerifier&&rights.Contains(PermissionCodes.CustomerService.InvoiceNotifications)))&&rights.Contains(PermissionCodes.Catalog.Customer.View)&&rights.Contains(PermissionCodes.Pos.Order.View);
        return new(user.Id,store.Id,name,rights.Contains(CustomerServiceRights.Reply),rights.Contains(CustomerServiceRights.Manage),canVerify);
    }
    public async Task<bool> ReplyAllowed(int userId,int storeId,CancellationToken ct) {
        var user=await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u=>u.Id==userId&&u.IsActive&&!u.IsDeleted).Select(u=>new{u.IsHostAdmin}).SingleOrDefaultAsync(ct);
        if(user is null)return false;if(user.IsHostAdmin)return true;
        var rights=await db.UserInStores.IgnoreQueryFilters().Where(m=>m.StoreId==storeId&&m.UserId==userId&&m.IsActive&&!m.IsDeleted&&m.Role.StoreId==storeId&&!m.Role.IsDeleted)
            .SelectMany(m=>m.Role.RolePermissions.Select(p=>p.Permission.Code)).Distinct().ToArrayAsync(ct);
        return rights.Contains(CustomerServiceRights.View)&&rights.Contains(CustomerServiceRights.Reply);
    }
    public async Task<bool> VerifierAllowed(int userId,int storeId,CancellationToken ct) {
        var user=await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u=>u.Id==userId&&u.IsActive&&!u.IsDeleted).Select(u=>new{u.IsHostAdmin}).SingleOrDefaultAsync(ct);
        if(user is null)return false;if(user.IsHostAdmin)return true;
        var rights=await db.UserInStores.IgnoreQueryFilters().Where(m=>m.StoreId==storeId&&m.UserId==userId&&m.IsActive&&!m.IsDeleted&&m.Role.StoreId==storeId&&!m.Role.IsDeleted)
            .SelectMany(m=>m.Role.RolePermissions.Select(p=>p.Permission.Code)).Distinct().ToArrayAsync(ct);
        return new[]{CustomerServiceRights.View,CustomerServiceRights.Verify,PermissionCodes.Catalog.Customer.View,PermissionCodes.Pos.Order.View}.All(rights.Contains);
    }
    public async Task<CustomerStaff> Verifier(CancellationToken ct) {
        var who=await Staff(CustomerServiceRights.Verify,ct);if(!who.CanVerify)throw AssistantException.Denied();return who;
    }
    public async Task<CustomerStaff> InvoiceNotificationVerifier(CancellationToken ct) {
        var who=await Staff(PermissionCodes.CustomerService.InvoiceNotifications,ct,invoiceNotificationVerifier:true);
        if(!who.CanVerify)throw AssistantException.Denied();return who;
    }
    public async Task Recheck(CustomerStaff original,string required,CancellationToken ct) {
        var now=await Staff(required,ct);if(original.UserId!=now.UserId||original.StoreId!=now.StoreId)throw AssistantException.Denied();
    }
}