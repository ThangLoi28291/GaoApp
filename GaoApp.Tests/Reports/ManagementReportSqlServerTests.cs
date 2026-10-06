using System.Net;
using System.Net.Http.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Reports;
using GaoApp.Tests.Security;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Reports;

[Collection("R1FinalDatabasePreflight")]
public sealed class ManagementReportSqlServerTests
{
    [Fact]
    public async Task Real_reports_and_expenses_enforce_permissions_versions_tenants_and_financial_totals()
    {
        await using var app=await FullApplicationFixture.StartAsync(); var store=app.Stores[0];
        using var owner=await app.LoginAsync(await app.AddAccountAsync(store,"*"));
        using var reader=await app.LoginAsync(await app.AddAccountAsync(store,PermissionCodes.Report.Profit.View));
        using var sales=await app.LoginAsync(await app.AddAccountAsync(store,PermissionCodes.Report.Sales.View));
        using var foreign=await app.LoginAsync(await app.AddAccountAsync(app.Stores[1],"*"));
        var day=TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time")).Date;
        var query=$"?fromDate={day:yyyy-MM-dd}&toDate={day:yyyy-MM-dd}&compare=none";
        await owner.JsonAsync(HttpMethod.Post,"/admin/pos/shift/open",new{openingCash=0,warehouseId=store.WarehouseId});
        var draft=await owner.JsonAsync(HttpMethod.Post,"/admin/pos/draft");var orderId=draft.GetProperty("orderId").GetInt32();
        await owner.JsonAsync(HttpMethod.Post,$"/admin/pos/{orderId}/items?variantId={store.VariantId}&qty=3");
        await owner.JsonAsync(HttpMethod.Post,$"/admin/pos/{orderId}/payments",new{clientRequestId=Guid.NewGuid(),amount=60,method=0});
        await owner.JsonAsync(HttpMethod.Post,$"/admin/pos/{orderId}/finalize");
        await using(var db=app.Database.CreateTenantContext(store.StoreId)) Assert.Equal("RETAIL",(await db.Orders.SingleAsync(x=>x.Id==orderId)).CustomerPriceTierSnapshot);
        var write=new ExpenseWriteDto{ClientRequestId=Guid.NewGuid(),Name="Thuê cửa hàng",Category="rent",Amount=90,RecognitionFrom=day,RecognitionTo=day.AddDays(2)};
        async Task<ExpenseRowDto> Post(string path,object body){using var response=await owner.Http.PostAsJsonAsync(path,body);Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());return (await response.Content.ReadFromJsonAsync<ExpenseRowDto>())!;}
        var expense=await Post("/admin/reports/expenses",write);
        Assert.Equal(expense.Id,(await Post("/admin/reports/expenses",write)).Id);
        Assert.NotEmpty(expense.RowVersion);
        write.RowVersion=expense.RowVersion;write.Name="Thuê cửa hàng đã kiểm tra";
        var edited=await Post($"/admin/reports/expenses/{expense.Id}/edit",write);
        using(var stale=await owner.Http.PostAsJsonAsync($"/admin/reports/expenses/{expense.Id}/confirm",new{rowVersion=expense.RowVersion})) Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
        var confirmed=await Post($"/admin/reports/expenses/{expense.Id}/confirm",new{rowVersion=edited.RowVersion});
        Assert.Equal("confirmed",confirmed.Status);
        var report=(await owner.Http.GetFromJsonAsync<ManagementReportDto>("/admin/reports/management/data"+query))!;
        Assert.Equal(60,report.Current.Summary.NetSales.Value);Assert.Equal(30,report.Current.Summary.Cogs.Value);
        Assert.Equal(30,report.Current.OperatingExpenses);Assert.Equal(0,report.Current.OperatingProfit);
        Assert.Equal(60,Assert.Single(report.Current.Products).Summary.NetSales.Value);
        Assert.Equal("RETAIL",Assert.Single(report.Current.CustomerGroups).Group);
        Assert.Contains("SL đơn vị gốc",await owner.Http.GetStringAsync("/admin/reports/management/export"+query+"&dimension=products"));
        Assert.DoesNotContain("SL đơn vị gốc",await owner.Http.GetStringAsync("/admin/reports/management/export"+query+"&dimension=customers"));
        var scoped=(await owner.Http.GetFromJsonAsync<ManagementReportDto>("/admin/reports/management/data"+query+"&terminalId="+store.TerminalId))!;Assert.Null(scoped.Current.OperatingProfit);
        using(var immutable=await owner.Http.PostAsJsonAsync($"/admin/reports/expenses/{expense.Id}/edit",write)) Assert.Equal(HttpStatusCode.Conflict,immutable.StatusCode);
        using(var denied=await reader.Http.PostAsJsonAsync("/admin/reports/expenses",write)) Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using(var denied=await reader.Http.PostAsJsonAsync($"/admin/reports/expenses/{expense.Id}/void",new{rowVersion=confirmed.RowVersion,reason="test"})) Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using(var denied=await reader.Http.GetAsync("/admin/reports/management/export"+query)) Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using(var denied=await sales.Http.GetAsync("/admin/reports/management/data"+query)) Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        Assert.Contains("data-sales-executive-report",await sales.Http.GetStringAsync("/admin/reports/overview"));
        Assert.DoesNotContain("data-management-report",await sales.Http.GetStringAsync("/admin/reports/overview"));
        foreach(var route in new[]{"overview","profit","products","categories","customers","groups","expenses"}) Assert.Contains("data-management-report",await owner.Http.GetStringAsync("/admin/reports/"+route));
        var foreignList=(await foreign.Http.GetFromJsonAsync<ExpenseListDto>("/admin/reports/expenses/data"+query))!;Assert.Empty(foreignList.Items);
        using(var denied=await foreign.Http.PostAsJsonAsync($"/admin/reports/expenses/{expense.Id}/void",new{rowVersion=confirmed.RowVersion,reason="test"})) Assert.Equal(HttpStatusCode.NotFound,denied.StatusCode);
        var token=owner.Http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();owner.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using(var csrf=await owner.Http.PostAsJsonAsync("/admin/reports/expenses",write)) Assert.Equal(HttpStatusCode.BadRequest,csrf.StatusCode);
        owner.Http.DefaultRequestHeaders.Add("RequestVerificationToken",token);
        // Expenses alone produce a negative result; CSV must retain a numeric minus sign.
        var foreignWrite=new ExpenseWriteDto{ClientRequestId=Guid.NewGuid(),Name="=SUM(1,2)",Amount=12,RecognitionFrom=day,RecognitionTo=day};
        var foreignDraft=(await foreign.JsonAsync(HttpMethod.Post,"/admin/reports/expenses",foreignWrite));
        await foreign.JsonAsync(HttpMethod.Post,$"/admin/reports/expenses/{foreignDraft.GetProperty("id").GetInt32()}/confirm",new{rowVersion=foreignDraft.GetProperty("rowVersion").GetString()});
        var csv=await foreign.Http.GetStringAsync("/admin/reports/management/export"+query);Assert.Matches("\"-12(?:\\.0+)?\"",csv);Assert.DoesNotContain("\"'-12",csv);
        await Post($"/admin/reports/expenses/{expense.Id}/void",new{rowVersion=confirmed.RowVersion,reason="Ghi trùng chứng từ"});
        var after=(await owner.Http.GetFromJsonAsync<ManagementReportDto>("/admin/reports/management/data"+query))!;Assert.Equal(0,after.Current.OperatingExpenses);Assert.Equal(30,after.Current.OperatingProfit);
    }
}
