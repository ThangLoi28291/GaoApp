namespace GaoApp.Web.Services.Acb;

public sealed record AcbCallbackAuthenticationResult(int Status, string? Code)
{
    public bool IsAuthenticated => Code is null;
}

/// <summary>Verify headers before MVC reads the body. Cache success only inside this HTTP request.</summary>
public static class AcbCallbackAuthentication
{
    private static readonly object Verified = new();
    private static readonly AcbCallbackAuthenticationResult Success = new(200, null);

    public static async Task<AcbCallbackAuthenticationResult> AuthenticateAsync(HttpContext context,
        AcbPaymentService service, AcbProtocol protocol, CancellationToken ct)
    {
        if (context.Items.ContainsKey(Verified)) return Success;
        var registered = context.Request.Headers["x-api-key"];
        var authorization = context.Request.Headers.Authorization;
        if (registered.Count > 1 || authorization.Count > 1) return new(403, "CONFLICTING_AUTH_HEADERS");
        var registeredKey = registered.ToString().Trim();
        var authorizationKey = authorization.ToString().Trim();
        if (authorizationKey.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) authorizationKey = authorizationKey[7..].Trim();
        if (registeredKey.Length > 0 && authorizationKey.Length > 0 && registeredKey != authorizationKey)
            return new(403, "CONFLICTING_AUTH_HEADERS");
        var settings = await service.SettingsAsync(ct);
        if (settings is null) return new(503, "STORE_ACB_NOT_CONFIGURED");
        try
        {
            if (!protocol.VerifyKey(settings, registeredKey.Length > 0 ? registeredKey : authorizationKey)) return new(403, "AUTH_REJECTED");
        }
        catch (System.Security.Cryptography.CryptographicException error)
        { context.Items["AcbCallbackFailure"] = error; return new(503, "CONFIG_DECRYPT_FAILED"); }
        // No cross-request cache: key rotation/revocation is checked again on the next request.
        context.Items[Verified] = true;
        return Success;
    }
}
