using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Web.Services.Assistant;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;
public sealed partial class AssistantIntegrationTests
{
    [Fact]
    public async Task Monitor_has_only_administration_rights_when_store_AI_is_off()
    {
        using var f=new Fixture();await f.Seed();var value=await f.Settings.ReadAsync(1,default);
        await f.Settings.SaveAsync(1,1,value with {Enabled=false},value.Version,default);
        Assert.Equal(403,(await Assert.ThrowsAsync<AssistantException>(()=>f.Access.CurrentAsync(null,default))).Status);
        var monitor=await f.Access.MonitorAsync(default);Assert.Equal(new AssistantScope("1",null,null),monitor.Scope);
        Assert.Equal(new[]{AssistantPermissions.Use,AssistantPermissions.Monitor},monitor.Permissions);
        var verified=await f.Access.RecheckAsync(monitor with {Permissions=[..monitor.Permissions,AssistantPermissions.Chat,AssistantPermissions.Catalog]},default);
        Assert.Equal(monitor.Permissions,verified.Permissions);
    }
    [Fact]
    public async Task Monitor_requires_current_settings_permission_and_revocation_invalidates_signed_tokens()
    {
        using var f=new Fixture();await f.Seed();var user=await f.Db.Users.SingleAsync(u=>u.Id==1);user.IsHostAdmin=false;
        f.Db.UserInStores.Add(new UserInStore {Id=2,StoreId=1,UserId=1,RoleId=1,IsActive=true,RowVersion=[1]});await f.Db.SaveChangesAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<AssistantException>(()=>f.Access.MonitorAsync(default))).Status);
        var permission=new Permission {Id=90,Code=PermissionCodes.System.Setting.Update,Name="TEST",GroupName="TEST"};
        var link=new RolePermission {RoleId=1,PermissionId=90,Permission=permission};f.Db.RolePermissions.Add(link);await f.Db.SaveChangesAsync();
        var signed=await f.Access.MonitorAsync(default);Assert.Contains(AssistantPermissions.Monitor,signed.Permissions);
        f.Db.RolePermissions.Remove(link);await f.Db.SaveChangesAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<AssistantException>(()=>f.Access.RecheckAsync(signed,default))).Status);
    }
}
