using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Services.Acb;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GaoApp.Web.Security;

/// <summary>Authenticated admission before MVC reads/deserializes a request body.</summary>
public sealed class ResourceProtectionFilter(ResourceAdmission admission, ICurrentUser user, ITenantContext tenant,
    AppDbContext db, AcbPaymentService bank, AcbProtocol protocol) : IAsyncResourceFilter, IOrderedFilter
{
    public int Order => -2500;
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var http = context.HttpContext;
        var policy = ResourceEndpointPolicy.For(http);
        if (policy is null) { await next(); return; }
        ResourceSubject subject = default;
        var actor = policy.Actor;
        if (policy.Ingress == ResourceGroup.BankIngress)
        {
            var authentication = await AcbCallbackAuthentication.AuthenticateAsync(http, bank, protocol, http.RequestAborted);
            if (!authentication.IsAuthenticated) { RejectBank(authentication.Status, authentication.Code!); return; }
            actor = ResourceGroup.BankAccepted;
        }
        else if (policy.Ingress == ResourceGroup.PhoneIngress)
        {
            var device = await BankNotificationDeviceAuthentication.AuthenticateAsync(db, http, http.RequestAborted);
            if (device is null) { context.Result = new UnauthorizedResult(); return; }
            subject = ResourceSubject.Device(device.Id);
        }
        else if (actor.HasValue)
        {
            if (!user.IsAuthenticated || user.UserId is not > 0) { context.Result = new UnauthorizedResult(); return; }
            if (actor is ResourceGroup.OfflineRead or ResourceGroup.OfflineControl or ResourceGroup.OfflineSync)
            {
                var device = PosDeviceSession.DeviceId(http.User);
                if (device is null) { context.Result = new UnauthorizedResult(); return; }
                subject = ResourceSubject.Device(device.Value);
            }
            else subject = ResourceSubject.User(user.UserId.Value);
        }
        if (!actor.HasValue) { await next(); return; }
        // Host administrators may access host reports; tenant data paths retain their existing scope checks.
        using var lease = admission.Enter(actor.Value, subject, tenant.StoreId is > 0 ? tenant.StoreId : null);
        if (!lease.IsAcquired) { context.Result = ResourceProtectionResponse.Result(http, lease); return; }
        await next();

        void RejectBank(int status, string code)
        {
            // Failed authentication never reaches model binding/inbox. Its IP budget cannot exclude a later valid callback.
            if (status == StatusCodes.Status403Forbidden)
            {
                using var failedAuthentication = admission.Enter(ResourceGroup.BankIngress, ResourceSubject.Ip(http.Connection.RemoteIpAddress));
                if (!failedAuthentication.IsAcquired)
                {
                    http.Items["AcbCallbackOutcome"] = "AUTH_RATE_LIMITED";
                    context.Result = ResourceProtectionResponse.Result(http, failedAuthentication);
                    return;
                }
            }
            http.Items["AcbCallbackOutcome"] = code;
            context.Result = new ObjectResult(new { code, diagnosticId = http.TraceIdentifier }) { StatusCode = status };
        }
    }
}
