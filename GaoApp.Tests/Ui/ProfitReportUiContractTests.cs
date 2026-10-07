using System.Diagnostics;
using System.Reflection;
using GaoApp.Application.Common.Security;
using GaoApp.Web.Areas.Admin.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Tests.Ui;

public sealed class ProfitReportUiContractTests
{
    [Fact]
    public void Management_workspace_preserves_accessible_states_and_shared_financial_views()
    {
        var html=Read("GaoApp.Web/Areas/Admin/Views/Shared/_ManagementReportWorkspace.cshtml");
        Assert.Contains("role=\"alert\"",html);Assert.Contains("aria-live=\"polite\"",html);
        Assert.Contains("data-report-results data-profit-results hidden",html);
        Assert.Contains("PermissionCodes.Report.Expenses.Manage",html);Assert.Contains("PermissionCodes.Report.Expenses.Confirm",html);
        Assert.Contains("@Html.AntiForgeryToken()",html);
        foreach(var view in new[]{"ProfitReport/Index","ManagementReport/Workspace","SalesExecutiveReport/Index"})
            Assert.Contains("_ManagementReportWorkspace",Read("GaoApp.Web/Areas/Admin/Views/"+view+".cshtml"));
        var js=Read("GaoApp.Web/wwwroot/Admin/js/reports/management-report.page.js");
        Assert.Contains("data-profit-trend-table",js);Assert.Contains("data-profit-detail-table",js);Assert.Contains("Xem bảng số liệu",js);
        Assert.Contains("RequestVerificationToken",js);
    }
    [Fact]
    public void Sales_overview_profit_navigation_preserves_permission_and_shared_filters()
    {
        var view = Read("GaoApp.Web/Areas/Admin/Views/SalesExecutiveReport/Index.cshtml");
        var reportStart = view.IndexOf("data-sales-executive-report", StringComparison.Ordinal);
        Assert.True(reportStart >= 0, "Sales report content boundary must exist.");
        var header = view[..reportStart];
        var navigation = System.Text.RegularExpressions.Regex.Match(header,
            @"@if\s*\(\(await\s+ProfitNavigationAuthorization\.AuthorizeAsync\(User,\s*PermissionCodes\.Report\.Profit\.View\)\)\.Succeeded\)\s*\{\s*(?<link><a\b.*?</a>)\s*\}",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        Assert.True(navigation.Success, "Profit navigation must be inside its successful Profit.View authorization guard.");
        var link = navigation.Groups["link"].Value;
        Assert.Contains("@Url.Action(\"Index\", \"ProfitReport\"", link, StringComparison.Ordinal);
        Assert.Contains("Lợi nhuận gộp", link, StringComparison.Ordinal);
        foreach (var field in new[] { "fromDate", "toDate", "compare", "terminalId", "customerState" })
            Assert.Contains($"Context.Request.Query[\"{field}\"]", link, StringComparison.Ordinal);
        Assert.Contains("['fromDate','toDate','compare','terminalId','customerState']", link, StringComparison.Ordinal);
        Assert.Contains("new URL(location.href).searchParams.get(k)", link, StringComparison.Ordinal);
        Assert.Contains("u.searchParams.delete(k)", link, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(typeof(ProfitReportController))]
    [InlineData(typeof(CostAdjustmentReportController))]
    public void Profit_routes_require_profit_permission_and_offer_only_get_actions(Type controller)
    {
        Assert.Equal(PermissionCodes.Report.Profit.View, controller.GetCustomAttribute<AuthorizeAttribute>(inherit: false)!.Policy);
        foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            Assert.NotNull(method.GetCustomAttribute<HttpGetAttribute>());
    }

    [Fact]
    public void Revised_kpi_contract_and_safe_text_rendering_have_no_numeric_finalization_ratio()
    {
        var js = Read("GaoApp.Web/wwwroot/Admin/js/reports/profit-report.page.js");
        Assert.Contains("Mức độ xác định giá vốn", js);
        Assert.DoesNotContain("FinalizationPercent", Read("GaoApp.Application/DTOs/Reports/Profit/ProfitReportDtos.cs"));
        Assert.DoesNotContain("innerHTML", js);
        Assert.Contains("fromDate", js); Assert.Contains("customerState", js);
        Assert.Contains("origin_", js);
        Assert.Contains("prefers-reduced-motion", Read("GaoApp.Web/wwwroot/Admin/css/reports/profit-report.css"));
        Assert.Contains(":focus-visible", Read("GaoApp.Web/wwwroot/Admin/css/reports/profit-report.css"));
    }

    [Theory]
    [InlineData("ProfitReport/Detail")]
    [InlineData("CostAdjustmentReport/Index")]
    public void Every_view_has_error_live_state_filters_and_table_alternative(string view)
    {
        var html = Read("GaoApp.Web/Areas/Admin/Views/" + view + ".cshtml");
        Assert.Contains("role=\"alert\"", html); Assert.Contains("aria-live=\"polite\"", html);
        Assert.Contains("data-profit-results hidden", html);
        Assert.Contains("data-profit-trend-table", html); Assert.Contains("data-profit-detail-table", html);
        Assert.Contains("Cách đọc báo cáo", html);
    }

    [Fact]
    public async Task Browser_script_invalidates_old_results_and_ignores_late_responses_on_failure()
    {
        // Execute the production script with a small DOM/fetch harness; no copied request-state implementation.
        var start = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("-e");
        start.ArgumentList.Add("""
const fs=require('fs'),vm=require('vm'),assert=require('assert');
class N {
 constructor(){this.hidden=false;this.value='';this.listeners={};this.children=[];this.attrs={};this.textContent='';this.classList={add(){}};}
 append(...v){this.children.push(...v);} prepend(...v){this.children.unshift(...v);}
 replaceChildren(...v){this.children=v;} setAttribute(k,v){this.attrs[k]=v;}
 addEventListener(k,f){this.listeners[k]=f;} scrollIntoView(){}
}
global.Node=N;global.Option=class extends N{constructor(text,value){super();this.textContent=text;this.value=String(value);}};
const elements=new Map(),get=k=>{if(!elements.has(k))elements.set(k,new N());return elements.get(k);};
const root=new N();root.dataset={mode:'detail',canSales:'false',url:'/admin/reports/profit/data'};
root.querySelector=get;root.querySelectorAll=()=>[];
global.document={querySelector:()=>root,createElement:()=>new N(),createElementNS:()=>new N()};
for(const key of ['[data-profit-filters]','[data-profit-detail-filter]'])get(key).elements={namedItem:name=>get(key+name)};
get('[data-profit-metric]').value='grossProfit';
global.location={href:'http://localhost/admin/reports/profit/detail?fromDate=2026-09-08&toDate=2026-09-08',origin:'http://localhost',assign(){}};
global.history={replaceState(a,b,u){location.href=String(u);}};
const pending=[];global.fetch=(u,o)=>new Promise((resolve,reject)=>pending.push({resolve,reject,url:String(u)}));
const flush=()=>new Promise(r=>setImmediate(r));
const m=v=>({value:v,quality:1});
const summary={netSales:m(200),cogs:m(100),grossProfit:m(100),grossMargin:m(50),provisionalCogs:m(0),costState:'Đã xác định',affectedOrders:0,affectedLines:0,isEmpty:false};
const data={query:{fromDate:'2026-09-08T00:00:00',toDate:'2026-09-08T00:00:00',compare:'none',terminalId:null,customerState:'all',page:1,pageSize:25},
generatedAtUtc:'2026-09-08T03:00:00Z',current:summary,comparison:null,trend:[{bucket:0,label:'08/09',summary}],comparisonTrend:[],details:[],totalItems:0,terminals:[]};
vm.runInThisContext(fs.readFileSync(process.argv[1],'utf8'));
(async()=>{
 assert.equal(pending.length,1);
 pending.shift().resolve({ok:true,json:async()=>data});await flush();
 assert.equal(get('[data-profit-results]').hidden,false,'initial results visible');
 get('[data-profit-refresh]').listeners.click();
 assert.equal(get('[data-profit-results]').hidden,true,'old snapshot immediately invalidated');
 pending.shift().resolve({ok:false,status:500});await flush();
 assert.equal(get('[data-profit-error]').hidden,false,'context error visible');
 assert.equal(root.attrs['aria-busy'],'false','busy ends on failure');
 assert.equal(get('[data-profit-results]').hidden,true,'old rows stay hidden');
 get('[data-profit-refresh]').listeners.click();const older=pending.shift();
 get('[data-profit-refresh]').listeners.click();const newer=pending.shift();
 newer.resolve({ok:false,status:403});await flush();
 older.resolve({ok:true,json:async()=>data});await flush();
 assert.equal(get('[data-profit-results]').hidden,true,'late success cannot replace newer permission error');
 assert.equal(root.attrs['aria-busy'],'false');
 assert.equal(pending.length,0,'no downstream fetch after context failure');
 console.log('PROFIT_UI_REQUEST_STATE_PASS');
})().catch(e=>{console.error(e);process.exitCode=1;});
""");
        start.ArgumentList.Add(Path.Combine(SourceRoot(), "GaoApp.Web/wwwroot/Admin/js/reports/profit-report.page.js"));
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await process.WaitForExitAsync(timeout.Token);
        Assert.True(process.ExitCode == 0, await stderr);
        Assert.Contains("PROFIT_UI_REQUEST_STATE_PASS", await stdout);
    }

    [Theory]
    [InlineData("ProfitReport/Detail")]
    public void Ux_remediation_views_expose_live_metric_heading(string view)
    {
        var html = Read("GaoApp.Web/Areas/Admin/Views/" + view + ".cshtml");
        Assert.Contains("<h2 data-profit-chart-title aria-live=\"polite\">", html);
    }

    [Fact]
    public void Ux_remediation_kpi_styles_preserve_numeric_and_negative_hierarchy()
    {
        var css = Read("GaoApp.Web/wwwroot/Admin/css/reports/profit-report.css");
        var button = System.Text.RegularExpressions.Regex.Match(css, @"\.profit-kpi button\{([^}]+)\}").Groups[1].Value;
        Assert.DoesNotContain("font:", button);
        Assert.DoesNotContain("color:", button);
        Assert.Contains("font-weight:700", css);
        Assert.Contains(".profit-kpi .profit-value.profit-negative{color:#a32a30}", css);
        Assert.Contains("[data-profit-chart]{overflow-x:auto;max-width:100%}", css);
    }

    [Theory]
    [InlineData("axes")]
    [InlineData("metric")]
    [InlineData("comparison")]
    [InlineData("no-comparison")]
    [InlineData("unequal-periods")]
    [InlineData("kpi")]
    [InlineData("validation")]
    [InlineData("unavailable")]
    public async Task Ux_remediation_executes_browser_contract(string scenario)
    {
        var start = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("-e");
        start.ArgumentList.Add("""
const fs=require('fs'),vm=require('vm'),assert=require('assert');
class N {
 constructor(tag='div'){this.tagName=tag;this.hidden=false;this.value='';this.listeners={};this.children=[];this.attrs={};this.ownText='';this.classList={add(){}};}
 get textContent(){return this.ownText+this.children.map(c=>c.textContent??String(c)).join('');}
 set textContent(v){this.ownText=String(v);this.children=[];}
 append(...v){this.children.push(...v);} prepend(...v){this.children.unshift(...v);}
 replaceChildren(...v){this.ownText='';this.children=v;} setAttribute(k,v){this.attrs[k]=String(v);}
 addEventListener(k,f){this.listeners[k]=f;} scrollIntoView(){}
}
global.Node=N;global.Option=class extends N{constructor(text,value){super('option');this.textContent=text;this.value=String(value);}};
const elements=new Map(),get=k=>{if(!elements.has(k))elements.set(k,new N());return elements.get(k);};
const root=new N();root.dataset={mode:'overview',canSales:'true',url:'/admin/reports/profit/data'};
root.querySelector=get;root.querySelectorAll=()=>[];
global.document={querySelector:()=>root,createElement:t=>new N(t),createElementNS:(ns,t)=>new N(t)};
for(const key of ['[data-profit-filters]','[data-profit-detail-filter]'])get(key).elements={namedItem:name=>get(key+name)};
get('[data-profit-metric]').value='grossProfit';
global.location={href:'http://localhost/admin/reports/profit?fromDate=2026-09-01&toDate=2026-09-03',origin:'http://localhost',assign(){}};
global.history={replaceState(a,b,u){location.href=String(u);}};
const pending=[];global.fetch=(u,o)=>new Promise((resolve,reject)=>pending.push({resolve,reject}));
const flush=()=>new Promise(r=>setImmediate(r)),m=(value,quality=1)=>({value,quality});
const summary={netSales:m(200),cogs:m(250),grossProfit:m(-50),grossMargin:m(-25),provisionalCogs:m(0),costState:'Đã xác định',affectedOrders:0,affectedLines:0,isEmpty:false};
const provisional={...summary,cogs:m(120,2),grossProfit:m(80,2),provisionalCogs:m(.25,2),costState:'Tạm tính'};
const unavailable={...summary,cogs:m(null,4),grossProfit:m(null,4),provisionalCogs:m(null,4),costState:'Chưa đủ dữ liệu'};
const data={query:{fromDate:'2026-09-01',toDate:'2026-09-03',compare:'previous',terminalId:null,customerState:'all',page:1,pageSize:25},
 period:{fromDate:'2026-09-01',toDate:'2026-09-03'},comparisonPeriod:{fromDate:'2026-08-29',toDate:'2026-08-31'},
 generatedAtUtc:'2026-09-08T03:00:00Z',current:summary,comparison:summary,
 trend:[{bucket:0,label:'01/09',summary},{bucket:1,label:'02/09',summary:unavailable},{bucket:2,label:'03/09',summary:provisional}],
 comparisonTrend:[{bucket:0,label:'29/08',summary:provisional},{bucket:1,label:'30/08',summary},{bucket:2,label:'31/08',summary}],details:[],totalItems:0,terminals:[]};
const scenario=process.argv[2];
if(scenario==='no-comparison'){data.comparison=null;data.comparisonTrend=[];}
if(scenario==='unequal-periods')data.comparisonTrend.push({bucket:3,label:'01/08 extra',summary});
const walk=n=>[n,...n.children.filter(c=>c instanceof N).flatMap(walk)];
const chart=()=>walk(get('[data-profit-chart]'));
const rows=()=>get('[data-profit-trend-table]').children.find(n=>n.tagName==='tbody').children;
vm.runInThisContext(fs.readFileSync(process.argv[1],'utf8'));
(async()=>{
 pending.shift().resolve({ok:true,json:async()=>data});await flush();
 assert.equal(get('[data-profit-results]').hidden,false,'production render must succeed');
 if(scenario==='axes'){
  const labels=chart().filter(n=>n.tagName==='text').map(n=>n.textContent);
  assert(labels.includes('0 ₫'),'readable zero');assert(labels.includes('-50 ₫'),'negative tick');
  assert(labels.includes('01/09')&&labels.includes('03/09'),'first and last dates');
  const dots=chart().filter(n=>n.tagName==='circle');
  assert(+dots[0].attrs.cy>+dots[1].attrs.cy,'negative below positive');
 }
 if(scenario==='metric'){
  for(const [value,title] of [['netSales','Net Sales'],['cogs','COGS — Giá vốn'],['grossProfit','Lợi nhuận gộp']]){
   get('[data-profit-metric]').value=value;get('[data-profit-metric]').listeners.change();
   assert.equal(get('[data-profit-chart-title]').textContent,'Xu hướng '+title);
   assert(chart().some(n=>n.tagName==='title'&&n.textContent.includes(title+':')));
  }
 }
 if(scenario==='comparison'){
  const tips=chart().filter(n=>n.tagName==='title').map(n=>n.textContent);
  assert(tips.some(t=>t.includes('Kỳ hiện tại • 01/09')&&t.includes('Kỳ trước • 29/08')),'real previous label alongside current');
  assert(tips.some(t=>t.includes('Tạm tính')&&t.includes('Giá vốn tạm tính: <1 ₫')));
  assert(tips.some(t=>t.includes('2026-08-29 → 2026-08-31')),'period dates disambiguate hourly/year-crossing labels');
  assert.equal(rows().length,6);
  assert.deepEqual(rows()[1].children.slice(0,2).map(n=>n.textContent),['Kỳ trước','29/08']);
  assert(rows()[1].textContent.includes('Tạm tính'));
  assert(chart().some(n=>n.tagName==='circle'&&n.attrs.fill==='white'),'provisional has hollow marker');
 }
 if(scenario==='no-comparison'){
  assert.equal(rows().length,3);assert(!get('[data-profit-trend-table]').textContent.includes('Kỳ trước'));
  assert(!get('[data-profit-legend]').textContent.includes('Nét đứt'));
  assert(chart().filter(n=>n.tagName==='title').every(n=>!n.textContent.includes('Kỳ trước')));
 }
 if(scenario==='unequal-periods'){
  assert.equal(rows().length,7);assert(rows()[6].textContent.includes('01/08 extra'),'extra previous bucket remains visible');
  assert(chart().some(n=>n.tagName==='title'&&n.textContent.includes('01/08 extra')));
 }
 if(scenario==='kpi'){
  const cards=get('[data-profit-kpis]').children;
  assert.equal(cards.length,6);
  assert.equal((cards[5].textContent.match(/Đã xác định/g)||[]).length,1,'categorical state once');
  assert(cards[2].children.some(n=>n.className?.includes('profit-negative')),'negative GP has semantic class');
  assert(!cards[5].textContent.includes('%'),'no invented finalization ratio');
 }
 if(scenario==='validation'){
  get('[data-profit-refresh]').listeners.click();
  assert.equal(get('[data-profit-results]').hidden,true);
  pending.shift().resolve({ok:false,status:400,json:async()=>({message:"Ngày bắt đầu không được lớn hơn ngày kết thúc. (Parameter 'request')"})});await flush();
  assert.equal(get('[data-profit-error] span').textContent,'Ngày bắt đầu không được lớn hơn ngày kết thúc.');
  assert.equal(get('[data-profit-error]').hidden,false);assert.equal(root.attrs['aria-busy'],'false');
  get('[data-profit-retry]').listeners.click();pending.shift().reject(new TypeError('internal transport details'));await flush();
  assert(!get('[data-profit-error] span').textContent.includes('internal'));
  get('[data-profit-retry]').listeners.click();pending.shift().resolve({ok:true,json:async()=>data});await flush();
  assert.equal(get('[data-profit-results]').hidden,false);assert.equal(get('[data-profit-error]').hidden,true);
 }
 if(scenario==='unavailable'){
  assert.equal(chart().filter(n=>n.tagName==='circle').length,5,'null must not become zero marker');
  const currentLine=chart().find(n=>n.tagName==='path'&&n.attrs.stroke==='#2459b7');
  assert.equal((currentLine.attrs.d.match(/M/g)||[]).length,2,'gap breaks line');
  assert(rows()[2].textContent.includes('Chưa đủ dữ liệu'));assert.equal(rows()[2].children[3].textContent,'—');
  get('[data-profit-metric]').value='netSales';get('[data-profit-metric]').listeners.change();
  assert.equal(chart().filter(n=>n.tagName==='circle').length,6,'trustworthy Net Sales remains visible');
 }
 console.log('UX_REMEDIATION_PASS '+scenario);
})().catch(e=>{console.error(e);process.exitCode=1;});
""");
        start.ArgumentList.Add(Path.Combine(SourceRoot(), "GaoApp.Web/wwwroot/Admin/js/reports/profit-report.page.js"));
        start.ArgumentList.Add(scenario);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await process.WaitForExitAsync(timeout.Token);
        Assert.True(process.ExitCode == 0, await stderr);
        Assert.Contains("UX_REMEDIATION_PASS " + scenario, await stdout);
    }

    private static string Read(string path) => File.ReadAllText(Path.Combine(SourceRoot(), path));
    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GaoApp.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Source root not found.");
    }
}
