using GaoApp.Application.DTOs.Security;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GaoApp.Web.Security;

public sealed class AdministrationConfirmationRateLimitFilter(LoginAccountLimiter limiter) : IAsyncActionFilter
{
    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        => context.ActionArguments.Values.OfType<SecurityActionConfirmation>()
            .Any(x => !string.IsNullOrEmpty(x.AdministratorPassword))
            ? new SelfPasswordRateLimitFilter(limiter).OnActionExecutionAsync(context, next)
            : next();
}
