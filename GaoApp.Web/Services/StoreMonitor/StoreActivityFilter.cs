using System.Security.Claims;
using GaoApp.Application.Common;
using GaoApp.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GaoApp.Web.Services.StoreMonitor;

/// <summary>Outermost action filter: publish after inner POS transaction filters have committed.</summary>
public sealed class StoreActivityFilter(ITenantContext tenant, StoreActivityRegistry registry,
    StoreActivityTicket tickets, AppDbContext db, ILogger<StoreActivityFilter> logger) : IAsyncActionFilter, IOrderedFilter
{
    public int Order => int.MinValue + 100;
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        StoreActivityWorkContext.Work? lineWork = null;
        StoreActivityEnricher.Snapshot? prior = null;
        if (!tenant.IsHostAdmin && tenant.StoreId is > 0 && context.HttpContext.User.Identity?.IsAuthenticated == true &&
            context.ModelState.IsValid && context.ActionDescriptor is ControllerActionDescriptor operation &&
            StoreActivityEnricher.NeedsBefore(operation.ControllerName, operation.ActionName))
        {
            try { prior = await StoreActivityEnricher.Read(db, tenant.StoreId.Value, operation.ControllerName, operation.ActionName,
                context.ActionArguments, null, null, context.HttpContext.RequestAborted); }
            catch (Exception ex) { logger.LogWarning(ex, "Unable to read prior activity facts"); }
        }
        if (!tenant.IsHostAdmin && tenant.StoreId is > 0 && context.ActionDescriptor is ControllerActionDescriptor before &&
            before.ControllerName is "StockCounts" or "StockTransfers" && before.ActionName is "UpdateLine" or "DeleteLine" &&
            context.ActionArguments.TryGetValue("lineId", out var line) && line is int lineId)
        {
            try { lineWork = await StoreActivityWorkContext.Line(db, tenant.StoreId.Value, before.ControllerName, lineId, context.HttpContext.RequestAborted); }
            catch (Exception ex) { logger.LogWarning(ex, "Unable to resolve warehouse activity parent"); }
        }
        var executed = await next();
        if (tenant.IsHostAdmin || tenant.StoreId is not > 0 || context.HttpContext.User.Identity?.IsAuthenticated != true ||
            !int.TryParse(context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0 ||
            executed.Exception is not null || executed.Canceled || !context.ModelState.IsValid ||
            context.ActionDescriptor is not ControllerActionDescriptor action) return;
        try
        {
            var (value, status) = executed.Result switch
            {
                ObjectResult result => (result.Value, result.StatusCode ?? 200),
                JsonResult result => (result.Value, result.StatusCode ?? 200),
                RedirectToActionResult when action.ControllerName == "StockDocumentManagement" && action.ActionName == "UpdateFreight" &&
                    context.Controller is Controller page && page.TempData.Peek("Success") is string && page.TempData.Peek("Error") is null => ((object?)null, 200),
                _ => ((object?)null, 0)
            };
            if (HttpMethods.IsGet(context.HttpContext.Request.Method) &&
                StoreActivityCatalog.PageModule(action.ControllerName) is { } pageModule)
            {
                var pageId = context.ActionArguments.TryGetValue("id", out var routeId) && routeId is int id ? (int?)id : null;
                if (executed.Result is ViewResult view && view.ViewData is not null)
                {
                    var work = await StoreActivityWorkContext.Page(db, tenant.StoreId.Value, action.ControllerName, pageId, context.HttpContext.RequestAborted);
                    view.ViewData["StoreActivityTicket"] = tickets.Issue(tenant.StoreId.Value, userId, pageModule, work?.Key, work?.Document);
                }
                else if (action.ControllerName == "LabelPrinting" && action.ActionName == "Detail" && status is >= 200 and < 300)
                {
                    var work = await StoreActivityWorkContext.Page(db, tenant.StoreId.Value, action.ControllerName, pageId, context.HttpContext.RequestAborted);
                    if (work is not null) context.HttpContext.Response.Headers["X-Gao-Activity-Ticket"] =
                        tickets.Issue(tenant.StoreId.Value, userId, pageModule, work.Key, work.Document);
                }
            }
            if (HttpMethods.IsGet(context.HttpContext.Request.Method) ||
                StoreActivityCatalog.Mutation(action.ControllerName, action.ActionName) is not { } mutation ||
                status is < 200 or >= 300 ||
                Property(value, "success") is false || Property(value, "isSuccess") is false ||
                Property(value, "duplicate") is true) return;

            var text = mutation.Text;
            if (action.ActionName == "AddPaymentAndMaybeFinalizeCurrentCart" && Property(value, "finalized") is true)
                text = "vừa thanh toán và chốt đơn";
            var description = StoreActivityDetails.Describe(action.ControllerName, action.ActionName, context.ActionArguments, value);
            if (action.ControllerName != "POS")
            {
                try
                {
                    var saved = await StoreActivityEnricher.Read(db, tenant.StoreId.Value, action.ControllerName, action.ActionName,
                        context.ActionArguments, value, prior, context.HttpContext.RequestAborted);
                    var detailed = saved is not null ? StoreActivityEnricher.Describe(action.ControllerName, action.ActionName, saved, prior,
                        StoreActivityEnricher.Request(context.ActionArguments)) : action.ControllerName == "LabelPrinting"
                        ? await StoreActivityEnricher.Label(db, tenant.StoreId.Value, action.ActionName, context.ActionArguments, value, context.HttpContext.RequestAborted) : null;
                    description = detailed ?? description;
                    text = description.Text ?? text;
                }
                catch (Exception ex) { logger.LogWarning(ex, "Unable to enrich saved store activity; using basic event"); }
            }
            var workKey = lineWork?.Key ?? description.WorkKey;
            if (action.ControllerName == "LabelPrinting" && action.ActionName is "Confirm" or "Cancel" &&
                context.ActionArguments.TryGetValue("id", out var labelJob) && labelJob is int labelJobId)
                workKey = (await StoreActivityWorkContext.LabelJob(db, tenant.StoreId.Value, labelJobId, context.HttpContext.RequestAborted))?.Key ?? workKey;
            registry.Record(tenant.StoreId.Value, userId, StoreActivityTicket.Person(context.HttpContext.User),
                mutation.Module, text, description.Text is not null ? description.Document : lineWork?.Document ?? description.Document, StoreActivityTicket.Terminal(context.HttpContext.User),
                action.ControllerName + "." + action.ActionName, description.Detail, workKey);
        }
        catch (Exception ex)
        {
            // Monitoring must never turn a successful business operation into an error.
            logger.LogWarning(ex, "Unable to update store activity wallboard for {Controller}/{Action}", action.ControllerName, action.ActionName);
        }
    }
    private static object? Property(object? value, string name) => value?.GetType().GetProperties()
        .FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && x.GetIndexParameters().Length == 0)?.GetValue(value);
}
