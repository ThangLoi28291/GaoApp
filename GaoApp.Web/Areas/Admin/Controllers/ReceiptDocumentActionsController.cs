using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin"), ApiController, Authorize, AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("admin/api/stock-documents/{id:int}/document-actions")]
public sealed class ReceiptDocumentActionsController(IReceiptDocumentActionsService service, IAuthorizationService authorization) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> Get(int id, CancellationToken ct) => Call(async () =>
    {
        var document = await service.GetAsync(id, ct);
        var rights = await Rights(document);
        if (!rights.Edit && !rights.Manage && !rights.Delete) return Forbid();
        return Ok(new { document, canRename = rights.Manage || rights.Edit && document.NeverSubmitted,
            canRequest = rights.Edit && document.Status == StockDocumentStatus.PendingApproval && document.Proposal == null,
            canDelete = rights.Delete && document.NeverSubmitted,
            canCorrect = document.Status == StockDocumentStatus.Confirmed && (rights.Edit || rights.Manage),
            canManage = rights.Manage });
    });

    [HttpPost("rename")]
    public Task<IActionResult> Rename(int id, ReceiptTitleRequest request, CancellationToken ct) => Call(async () =>
    {
        var rights = await Rights(await service.GetAsync(id, ct));
        if (!rights.Edit && !rights.Manage) return Forbid();
        await service.RenameAsync(id, request, rights.Manage, ct); return Ok(new { ok = true });
    });
    [HttpPost("request-title")]
    public Task<IActionResult> RequestTitle(int id, ReceiptTitleRequest request, CancellationToken ct) => Call(async () =>
    {
        if (!(await Rights(await service.GetAsync(id, ct))).Edit) return Forbid();
        await service.RequestTitleAsync(id, request, ct); return Ok(new { ok = true });
    });
    [HttpPost("review-title")]
    public Task<IActionResult> Review(int id, ReviewReceiptTitleRequest request, CancellationToken ct) => Call(async () =>
    {
        if (!(await Rights(await service.GetAsync(id, ct))).Manage) return Forbid();
        await service.ReviewTitleAsync(id, request, ct); return Ok(new { ok = true });
    });
    [HttpPost("delete")]
    public Task<IActionResult> Delete(int id, ReceiptVersionRequest request, CancellationToken ct) => Call(async () =>
    {
        if (!(await Rights(await service.GetAsync(id, ct))).Delete) return Forbid();
        await service.DeleteDraftAsync(id, request, ct); return Ok(new { ok = true });
    });
    private async Task<(bool Edit, bool Manage, bool Delete)> Rights(ReceiptDocumentActionsDto document)
    {
        var prefix = document.ReceiptSource == PurchaseReceiptSource.PurchaseOrder ? "purchase.receipt." : "inventory.stockdocument.";
        return ((await authorization.AuthorizeAsync(User, prefix + "update")).Succeeded,
            (await authorization.AuthorizeAsync(User, prefix + "approve")).Succeeded,
            (await authorization.AuthorizeAsync(User, prefix + "delete")).Succeeded);
    }
    private static async Task<IActionResult> Call(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (BusinessRuleException error) { return new ConflictObjectResult(new { message = error.SafeMessage }); }
    }
}
