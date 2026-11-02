namespace GaoApp.Web.Security;

/// <summary>Runs after trusted forwarded headers/routing, before tenant SQL, authentication and model binding.</summary>
public sealed class ResourceIngressMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ResourceAdmission admission)
    {
        var policy = ResourceEndpointPolicy.For(context);
        // A bank IP can carry many legitimate payments. Authenticate ACB before parsing its body;
        // never reject a payment callback because this IP/process admission bucket is full.
        // Invalid credentials are charged separately by ResourceProtectionFilter.
        if (policy is null || policy.Ingress == ResourceGroup.BankIngress) { await next(context); return; }
        using var lease = admission.Enter(policy.Ingress, ResourceSubject.Ip(context.Connection.RemoteIpAddress));
        if (!lease.IsAcquired) { await ResourceProtectionResponse.WriteAsync(context, lease); return; }
        await next(context);
    }
}
