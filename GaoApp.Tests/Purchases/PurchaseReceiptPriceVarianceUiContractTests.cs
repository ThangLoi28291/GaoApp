using FluentAssertions;
using GaoApp.Application.DTOs.Inventory;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Diagnostics;

namespace GaoApp.Tests.Purchases;

public sealed class PurchaseReceiptPriceVarianceUiContractTests
{
    [Theory]
    [InlineData("top")]
    [InlineData("commercial-entry")]
    [InlineData("outside-blocked")]
    [InlineData("invalid-price")]
    [InlineData("missing-module")]
    [InlineData("legacy")]
    public async Task Approval_entry_points_share_price_confirmation_preflight(string scenario)
    {
        var start = new ProcessStartInfo("node")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };
        start.ArgumentList.Add("-e");
        start.ArgumentList.Add("""
const fs=require('fs'),vm=require('vm'),assert=require('assert');
class Element {
 constructor(){this.dataset={};this.value='';this.checked=false;this.children=[];this.textContent='';this.classes=new Set();
  this.classList={add:k=>this.classes.add(k),remove:k=>this.classes.delete(k),toggle:(k,on)=>on?this.classes.add(k):this.classes.delete(k)};}
 addEventListener(){} focus(){} select(){} scrollIntoView(){}
 querySelector(){return null;} replaceChildren(){this.children=[];} appendChild(x){this.children.push(x);}
 click(){this.onclick?.();}
}
const scenario=process.argv[3],elements=new Map();
for(const id of ['btnOpenApproveModal','approveModal','approveNote','approveMessage','commercialApprovalWorkbench',
 'commercialSupplierId','commercialValidationMessage','priceVarianceAcceptancePanel','priceVarianceAcceptanceDetails',
 'acceptPriceVariance','managerApprovalBlockerSummary'])elements.set(id,new Element());
const get=id=>elements.get(id)||null,price=new Element(),last=new Element(),row=new Element();
price.value='30000';last.dataset.value='3000';row.dataset.lineNo='1';
row.querySelector=s=>s==='.commercial-unit-price'?price:s==='.commercial-last-price'?last:new Element();
get('commercialSupplierId').value='1';get('priceVarianceAcceptancePanel').classes.add('d-none');
global.window=global;global.setTimeout=()=>0;
global.document={getElementById:get,addEventListener(){},querySelector(){return null;},createElement:()=>new Element(),
 querySelectorAll:s=>s==='#commercialApprovalWorkbench .commercial-line'?[row]:[]};
global.fetch=()=>{throw new Error('Opening confirmation must not post or fetch');};
vm.runInThisContext(fs.readFileSync(process.argv[1],'utf8'));
vm.runInThisContext(fs.readFileSync(process.argv[2],'utf8'));
global.opens=0;vm.runInThisContext('approveModalInstance={show(){global.opens++;}};bindOpenApprovalModals();');
if(scenario==='outside-blocked')get('commercialApprovalWorkbench').dataset.hasPendingOutside='true';
if(scenario==='invalid-price')price.value='0';
if(scenario==='missing-module')delete global.GaoAppPurchaseReceiptApproval;
if(scenario==='legacy'){elements.delete('commercialApprovalWorkbench');delete global.GaoAppPurchaseReceiptApproval;}
const open=()=>scenario==='commercial-entry'?global.GaoAppPurchaseReceiptApproval.open():get('btnOpenApproveModal').click();
open();
if(['outside-blocked','invalid-price','missing-module'].includes(scenario)){
 assert.equal(opens,0,'Blocked commercial receipt must not open legacy confirmation');
}else{
 assert.equal(opens,1,'Open exactly once, without recursive click');
 if(scenario!=='legacy'){
  assert(!get('priceVarianceAcceptancePanel').classes.has('d-none'),'Both entries must display price-variance acceptance');
  assert(get('priceVarianceAcceptanceDetails').children[0].textContent.includes('30.000'),'Current price is displayed');
  get('acceptPriceVariance').checked=true;price.value='26000';open();
  assert.equal(opens,2);assert.equal(get('acceptPriceVariance').checked,false,'Reopening must reset acceptance');
  assert(get('priceVarianceAcceptanceDetails').children[0].textContent.includes('26.000'),'Reopening recomputes changed price');
 }
}
""");
        var root = FindRepositoryRoot();
        start.ArgumentList.Add(Path.Combine(root, "GaoApp.Web/wwwroot/Admin/js/stock-document-management.js"));
        start.ArgumentList.Add(Path.Combine(root, "GaoApp.Web/wwwroot/Admin/js/purchase-receipt-approval.js"));
        start.ArgumentList.Add(scenario);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        process.ExitCode.Should().Be(0, await output + await error);
    }

    [Fact]
    public void Confirmation_exposes_explicit_acceptance_and_keeps_reason_optional()
    {
        var edit = Read("GaoApp.Web/Areas/Admin/Views/StockDocumentManagement/Edit.cshtml");
        var script = Read("GaoApp.Web/wwwroot/Admin/js/purchase-receipt-approval.js");

        edit.Should().Contain("id=\"priceVarianceAcceptancePanel\"");
        edit.Should().Contain("id=\"acceptPriceVariance\"");
        edit.Should().Contain("Không bắt buộc nhập lý do chỉ vì giá thay đổi.");
        edit.Should().NotContain("required id=\"acceptPriceVariance\"");
        script.Should().Contain("acceptPriceVariance:");
        script.Should().Contain("expectedLastPurchaseUnitPriceBeforeVat:");
        script.Should().Contain("getPriceVariances().length > 0");
        script.Should().Contain("renderPriceVarianceAcceptance()");
        script.Should().Contain("const lastPrice = readLastPurchasePrice(row)");
        script.Should().Contain("expectedLastPurchaseUnitPriceBeforeVat: lastPrice");
        script.Should().Contain("previous === null");
        script.Should().Contain("if (previous === 0)");
        script.Should().NotContain("lastPrice > 0 ? roundMoney(lastPrice) : null");
    }

    [Fact]
    public void Expected_snapshot_accepts_zero_but_current_purchase_price_remains_positive()
    {
        var range = typeof(PurchaseReceiptFinancialLineInputDto)
            .GetProperty(nameof(PurchaseReceiptFinancialLineInputDto
                .ExpectedLastPurchaseUnitPriceBeforeVat))!
            .GetCustomAttribute<RangeAttribute>();

        range.Should().NotBeNull();
        range!.Minimum.Should().Be("0");
    }

    [Fact]
    public void Authoritative_service_recomputes_history_and_rejects_missing_acceptance()
    {
        var service = Read("GaoApp.Application/Services/Inventory/StockDocumentService.cs");

        service.Should().Contain("GetLastPurchaseBaseUnitPricesBeforeVatAsync");
        service.Should().Contain("PurchaseReceiptPriceVariancePolicy.Evaluate");
        service.Should().Contain("authoritativeLastUnitPrice != expectedLastUnitPrice");
        service.Should().Contain("priceVariances.Count > 0 && !acceptPriceVariance");
        service.Should().Contain("LockPurchasePriceHistoryVariantsAsync");
        service.Should().Contain("Đã xác nhận chênh lệch giá nhập so với lần gần nhất");
    }

    [Fact]
    public void Commercial_workbench_uses_server_resolved_nullable_price_and_explains_missing_price()
    {
        var workbench = Read(
            "GaoApp.Web/Areas/Admin/Views/StockDocumentManagement/_CommercialApprovalWorkbench.cshtml");

        workbench.Should().Contain("EditableUnitPriceBeforeVat");
        workbench.Should().Contain("Chưa có giá nhập — quản lý cần nhập giá trước khi duyệt.");
        workbench.Should().NotContain(
            "value=\"@line.UnitPriceBeforeVat.ToString(\"0.##\", CultureInfo.InvariantCulture)\"");
    }

    [Fact]
    public void Commercial_price_and_tax_changes_request_debounced_abortable_server_preview()
    {
        var script = Read("GaoApp.Web/wwwroot/Admin/js/purchase-receipt-approval.js");

        script.Should().Contain("scheduleCommercialReconciliationPreview");
        script.Should().Contain("/input-invoices/reconciliation/preview");
        script.Should().Contain("AbortController");
        script.Should().Contain("previewRequestVersion");
        script.Should().Contain("window.renderInputInvoiceReconciliationPreview");
    }

    [Fact]
    public void Commercial_preview_participates_in_shared_reconciliation_render_generation()
    {
        var script = Read("GaoApp.Web/wwwroot/Admin/js/purchase-receipt-approval.js");

        script.Should().Contain("beginInputInvoiceReconciliationRenderRequest");
        script.Should().Contain("reconciliationRenderGeneration");
        script.Should().Contain(
            "window.renderInputInvoiceReconciliationPreview?.(api.data, reconciliationRenderGeneration)");
    }

    [Fact]
    public void Xml_workspace_can_request_immediate_preview_from_current_commercial_dom()
    {
        var script = Read("GaoApp.Web/wwwroot/Admin/js/purchase-receipt-approval.js");

        script.Should().Contain(
            "refreshReconciliationPreview: refreshCommercialReconciliationPreview");
        script.Should().Contain("async function refreshCommercialReconciliationPreview()");
        script.Should().Contain("commercialReconciliationPreviewTimer = null");
        script.Should().Contain("const requestVersion = ++previewRequestVersion");
        script.Should().Contain("await requestCommercialReconciliationPreview(requestVersion)");
        script.Should().Contain("getCommercialRows().map");
        script.Should().Contain("row.querySelector('.commercial-unit-price')");
    }

    private static string Read(string relativePath)
        => File.ReadAllText(Path.Combine(FindRepositoryRoot(),
            relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "GaoApp.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
