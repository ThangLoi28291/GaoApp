using GaoApp.Application.Interfaces.Services.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GaoApp.Web.Common.POS;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class StoreAdminOnlyAttribute() : TypeFilterAttribute(typeof(StoreAdminOnlyFilter));

public sealed class StoreAdminOnlyFilter(IStoreAdminAccess access) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (!await access.IsAdminAsync(context.HttpContext.RequestAborted))
            context.Result = new ForbidResult();
    }
}
