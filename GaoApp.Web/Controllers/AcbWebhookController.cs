using System.Globalization;
using System.Text.Json;
using GaoApp.Web.Services.Acb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Controllers;

[AllowAnonymous, IgnoreAntiforgeryToken]
[Route("api/acb/webhook")]
[Route("Admin/api-callback")]
public sealed class AcbWebhookController(AcbPaymentService service, AcbProtocol protocol, AcbCallbackInbox inbox) : Controller
{
    [HttpPost, RequestSizeLimit(4194304)]
    public async Task<IActionResult> Receive([FromBody] JsonElement payload, CancellationToken ct)
    {
        var settings = await service.SettingsAsync(ct);
        // This merchant registered x-api-key. Authorization remains compatible with ACB's documented API-key mode.
        var registeredKey = Request.Headers["x-api-key"].ToString().Trim();
        var authorizationKey = Request.Headers.Authorization.ToString().Trim();
        if (authorizationKey.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) authorizationKey = authorizationKey[7..].Trim();
        if (registeredKey.Length > 0 && authorizationKey.Length > 0 && registeredKey != authorizationKey)
            return Rejected(403, "CONFLICTING_AUTH_HEADERS");
        var key = registeredKey.Length > 0 ? registeredKey : authorizationKey;
        if (settings == null) return Rejected(503, "STORE_ACB_NOT_CONFIGURED");
        try
        {
            if (!protocol.VerifyKey(settings, key)) return Rejected(403, "AUTH_REJECTED");
        }
        catch (System.Security.Cryptography.CryptographicException error) { HttpContext.Items["AcbCallbackFailure"] = error; return Rejected(503, "CONFIG_DECRYPT_FAILED"); }
        if (!ModelState.IsValid) return Rejected(400, "INVALID_JSON");
        int receiptId;
        try { receiptId = await inbox.AcceptAsync(payload, ct); }
        catch (AcbCallbackValidationException error) { return Rejected(400, error.Code); }
        catch (ArgumentException) { return Rejected(400, "INVALID_ENVELOPE"); }
        catch (AcbCallbackConflictException) { return Rejected(409, "DUPLICATE_PAYLOAD_CONFLICT"); }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException error) { HttpContext.Items["AcbCallbackFailure"] = error; return Rejected(503, "INBOX_SAVE_FAILED"); }
        HttpContext.Items["AcbCallbackOutcome"] = "ACCEPTED";
        HttpContext.Items["AcbCallbackReceiptId"] = receiptId;
        // Acknowledge only committed evidence. Processing/retry survives HTTP disconnection and restart.
        return Json(new
        {
            requestTrace = AcbProtocol.Text(payload, "requestTrace"),
            responseDateTime = AcbProtocol.FormatDateTime(DateTimeOffset.UtcNow),
            responseStatus = new { responseCode = "00000000", responseMessage = "Success" },
            responseBody = new { index = 1, referenceCode = receiptId.ToString(CultureInfo.InvariantCulture) }
        });
    }

    private ObjectResult Rejected(int status, string code)
    {
        HttpContext.Items["AcbCallbackOutcome"] = code;
        return StatusCode(status, new { code, diagnosticId = HttpContext.TraceIdentifier });
    }
}
