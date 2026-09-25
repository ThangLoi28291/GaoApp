using System.Reflection;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Web.Areas.Admin.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Tests.Security;

public sealed class EndpointSecurityCoverageTests
{
    [Fact]
    public void Every_admin_action_has_a_permission_or_an_explicit_resource_authorization_contract()
    {
        // These actions check permissions and document ownership inside the action.
        // Keep action names explicit so adding an unprotected action fails this audit.
        var resourceChecks = new HashSet<string>(StringComparer.Ordinal)
        {
            // Store-admin/runtime authorization is enforced by StoreAdminOnly and/or
// the application service; stock-document completion selects the approve
// policy dynamically from the receipt source.
"POSShift.ConfirmCashReceipt",
"POSShiftHandoverSlip.Assignments",
"POSShiftHandoverSlip.Update",
"StockDocumentManagement.CompleteLegacyCatalogProduct",
            // Existing barcode actions authorize against their receipt source (purchase or inventory)
            // inside each action through Has(context, action); there is no single controller policy.
            "ReceiptBarcodeProposals.Get", "ReceiptBarcodeProposals.Lookup", "ReceiptBarcodeProposals.Units",
            "ReceiptBarcodeProposals.Propose", "ReceiptBarcodeProposals.Review",
            "ReceiptIntake.Get", "ReceiptIntake.Capture", "ReceiptIntake.Known", "ReceiptIntake.Review", "ReceiptIntake.Remove", "ReceiptIntake.Quantity",
            // Photo checks the receipt's store and source-specific view/update/approve permission;
            // ReceivingPackagingPhotoHttpTests covers forbidden and cross-store access.
            "ReceiptIntake.Photo",
            "StockDocumentProvisionalItems.Get", "StockDocumentProvisionalItems.Options",
            "StockDocumentProvisionalItems.CaptureOptions", "StockDocumentProvisionalItems.Candidates",
            "StockDocumentProvisionalItems.Capture", "StockDocumentProvisionalItems.Increment",
            "StockDocumentProvisionalItems.Edit", "StockDocumentProvisionalItems.Remove",
            "StockDocumentProvisionalItems.Undo", "StockDocumentProvisionalItems.Link",
            "StockDocumentProvisionalItems.QuickCreate",
            // Procurement applies different policies to own/store records, document
            // sources and states. These explicitly reviewed actions retain those checks.
            "PurchaseOrders.Index", "PurchaseOrders.GetPurchaseOrderIndexData",
            "PurchaseOrders.GetPurchaseOrderIndexFilterOptions", "PurchaseOrders.GetPurchaseOrderQuickView",
            "PurchaseOrders.SupplierLookup", "PurchaseOrders.ProductLookup", "PurchaseOrders.Save", "PurchaseOrders.Details",
            "PurchaseReceiving.Index",
            "PurchaseRequests.Index", "PurchaseRequests.GetPurchaseRequestIndexData",
            "PurchaseRequests.GetPurchaseRequestRequesterOptions", "PurchaseRequests.GetPurchaseRequestQuickView",
            "PurchaseRequests.ProductLookup", "PurchaseRequests.Save", "PurchaseRequests.Details",
            "StockDocumentManagement.Index", "StockDocumentManagement.GetReceiptFormOptions",
            "StockDocumentManagement.Edit", "StockDocumentManagement.UpdateHeader",
            "StockDocumentManagement.UpdateFreight", "StockDocumentManagement.LinesTable",
            "StockDocuments.GetVariantUnits", "StockDocuments.GetReceiptList", "StockDocuments.GetReceiptFormOptions",
            "StockDocuments.SearchProducts", "StockDocuments.SubmitApproval", "StockDocuments.Approve",
            "StockDocuments.ApproveCommercial", "StockDocuments.Reject", "StockDocuments.BrowseInputInvoicePicker",
            "StockDocuments.GetInputInvoiceAssociation", "StockDocuments.PreviewInputInvoicePdf",
            "StockDocuments.PreviewInputInvoiceXml", "StockDocuments.SelectInputInvoiceFromPicker",
            "StockDocuments.PreviewLinkedInputInvoicePdf", "StockDocuments.PreviewLinkedInputInvoiceXml",
            "StockDocuments.UnlinkInputInvoice", "StockDocuments.RelinkInputInvoice", "StockDocuments.GetInputInvoices",
            "StockDocuments.GetInputInvoiceLineMaps", "StockDocuments.UpdateInputInvoiceLineMap",
            "StockDocuments.BulkUpdateInputInvoiceLineMaps", "StockDocuments.GetInputInvoiceReconciliation",
            "StockDocuments.PreviewInputInvoiceReconciliation", "StockDocuments.IgnoreInputInvoiceDetail",
            "StockDocuments.UnignoreInputInvoiceDetail", "StockDocuments.AcceptInputInvoiceReconciliation",
            "StockDocuments.RequestRevision", "StockDocuments.ResolveRevisionRequest"
        };
        // Password self-service derives identity from the principal and verifies current credentials.
        var authenticationOnly = new HashSet<string> { "Account.Logout", "EmployeeAccount.ChangePassword" };
        var anonymous = new HashSet<string> { "Account.Login", "Account.AccessDenied" };
        var findings = new List<string>();
        var inventory = new List<object>();
        foreach (var type in typeof(ProductController).Assembly.GetTypes().Where(t => !t.IsAbstract &&
                     typeof(ControllerBase).IsAssignableFrom(t) && t.GetCustomAttribute<AreaAttribute>(true)?.RouteValue == "Admin"))
        foreach (var action in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                     .Where(m => !m.IsSpecialName && !m.IsDefined(typeof(NonActionAttribute), true)))
        {
            var key = type.Name.Replace("Controller", "") + "." + action.Name;
            var authorization = type.GetCustomAttributes<AuthorizeAttribute>(true)
                .Concat(action.GetCustomAttributes<AuthorizeAttribute>(true)).ToArray();
            var allowAnonymous = type.IsDefined(typeof(AllowAnonymousAttribute), true) || action.IsDefined(typeof(AllowAnonymousAttribute), true);
            var policies = authorization.Select(a => a.Policy).Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
            var roles = authorization.Select(a => a.Roles).Where(r => !string.IsNullOrWhiteSpace(r)).ToArray();
            inventory.Add(new { action = key, allowAnonymous, policies, roles, resourceAuthorization = resourceChecks.Contains(key) });
            if (allowAnonymous)
            {
                if (!anonymous.Contains(key)) findings.Add(key + ": unexpected anonymous action");
            }
            else if (policies.Length == 0 && roles.Length == 0 &&
                     !(authorization.Length > 0 && (resourceChecks.Contains(key) || authenticationOnly.Contains(key))))
                findings.Add(key + ": missing business permission");
        }
        var root = Path.Combine(FullApplicationFixture.SourceRoot(), "TestResults", "security-phase7");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "admin-permission-inventory.json"), JsonSerializer.Serialize(inventory, new JsonSerializerOptions { WriteIndented = true }));
        Assert.True(findings.Count == 0, string.Join('\n', findings));
        Assert.True(inventory.Count > 200, "The audit must cover the complete Admin assembly.");
    }

    [Fact]
    public void New_tax_and_promotion_permissions_are_seedable_and_unique()
    {
        var codes = PermissionCatalog.All.Select(p => p.Code).ToArray();
        Assert.Equal(codes.Length, codes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(PermissionCodes.Catalog.Customer.ManageRewards, codes);
        foreach (var type in new[] { typeof(PermissionCodes.Catalog.Tax), typeof(PermissionCodes.Catalog.Promotion), typeof(PermissionCodes.Catalog.DisplayPromotion), typeof(PermissionCodes.Pos.Order) })
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            Assert.Contains((string)field.GetRawConstantValue()!, codes);
    }
}
