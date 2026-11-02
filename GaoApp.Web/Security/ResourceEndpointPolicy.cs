using ChatbotLocal.Integration;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace GaoApp.Web.Security;

public sealed record ResourceEndpointPolicy(ResourceGroup Ingress, ResourceGroup? Actor = null)
{
    private static readonly HashSet<string> Reports = new(StringComparer.Ordinal)
    { "SalesReport", "SalesExecutiveReport", "ManagementReport", "ProfitReport", "OperationsReport", "CostAdjustmentReport", "AssistantQuality" };
    public static ResourceGroup AssistantOperationGroup(AssistantOperation action) => action switch
    {
        AssistantOperation.RequestStatus or AssistantOperation.CancelRequest => ResourceGroup.AssistantControl,
        AssistantOperation.Chat or AssistantOperation.Quote or AssistantOperation.Receiving or AssistantOperation.NormalizeName or
        AssistantOperation.CreateConversation or AssistantOperation.ResumeConversation or AssistantOperation.RenameConversation or
        AssistantOperation.DeleteConversation or AssistantOperation.Feedback => ResourceGroup.AssistantWork,
        _ => ResourceGroup.AssistantRead
    };
    public static ResourceEndpointPolicy? For(HttpContext context)
    {
        var action = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        if (action is null) return null;
        var controller = action.ControllerName;
        if (controller is "Assistant" or "AssistantSearch" or "AssistantSearchAdmin" or "AssistantKnowledge") return new(ResourceGroup.AssistantIngress);
        if (controller is "AssistantSource" or "AssistantKnowledgeSource") return new(ResourceGroup.AssistantSourceIngress);
        if (controller is "CustomerSupport" or "CustomerBusiness" or "CustomerChannelWebhook") return new(ResourceGroup.CustomerIngress);
        if (controller is "CustomerInbox" or "CustomerVerification" or "CustomerChannels" or "CustomerQuality") return new(ResourceGroup.AssistantIngress);
        if (controller == "CustomerAssistantSource") return new(ResourceGroup.AssistantSourceIngress);
        if (controller == "AcbWebhook") return new(ResourceGroup.BankIngress);
        if (controller == "BankNotificationDevice") return new(ResourceGroup.PhoneIngress,
            action.ActionName == "Heartbeat" ? ResourceGroup.PhoneHeartbeat : ResourceGroup.PhoneEvents);
        if (controller == "POSOffline") return new(ResourceGroup.OfflineIngress, HttpMethods.IsGet(context.Request.Method)
            ? action.ActionName == "Status" ? ResourceGroup.OfflineControl : ResourceGroup.OfflineRead : ResourceGroup.OfflineSync);
        // Header selects only a fixed policy, never a partition key. Actor/device still comes from the validated ticket.
        if (context.Request.Path.StartsWithSegments("/admin/pos", StringComparison.OrdinalIgnoreCase) && !HttpMethods.IsGet(context.Request.Method)
            && context.Request.Headers.ContainsKey("X-POS-Operation-Id")) return new(ResourceGroup.OfflineIngress, ResourceGroup.OfflineSync);
        if (action.Parameters.Any(x => typeof(IFormFile).IsAssignableFrom(x.ParameterType) || typeof(IEnumerable<IFormFile>).IsAssignableFrom(x.ParameterType)) ||
            controller == "Media" && action.ActionName == "TempUpload" || controller == "InputInvoiceLibrary" && action.ActionName == "Sync")
            return new(ResourceGroup.UploadIngress, ResourceGroup.Upload);
        if ((HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)) &&
            (controller == "Invoice" && action.ActionName is "Index" or "ViettelDashboard" ||
             controller == "InvoiceDashboard" && action.ActionName == "Index"))
            return new(ResourceGroup.HeavyIngress, ResourceGroup.HeavyRead);
        if (controller == "AuditLogsApi" && action.ActionName is "GetList" or "GetDetail" ||
            controller == "InvoiceIntegrationLog" && action.ActionName is "Index" or "Detail")
            return new(ResourceGroup.HeavyIngress, ResourceGroup.HeavyRead);
        if (Reports.Contains(controller) || HttpMethods.IsGet(context.Request.Method) &&
            ((controller is "Product" or "ProductAttribute") && action.ActionName is "Index" or "Search" || controller == "POS" && action.ActionName is "SearchProducts" or "SearchCustomers") ||
            controller == "InputInvoiceLibrary" && action.ActionName is "List" or "Detail" or "Preview" or "Pdf" or "Xml" ||
            controller == "MediaLibrary" && action.ActionName is "Data" or "Index" or "Detail" or "Cleanup" or "CleanupOne")
            return new(ResourceGroup.HeavyIngress, ResourceGroup.HeavyRead);
        return null;
    }
}
