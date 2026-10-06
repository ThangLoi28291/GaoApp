using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Reports;
using GaoApp.Application.DTOs.Reports.Profit;
using GaoApp.Application.DTOs.Reports.Sales;
using GaoApp.Application.Interfaces.Repositories.Reports;
using GaoApp.Application.Services.Reports;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Services.Reports;
using GaoApp.Infrastructure.Repositories.Reports;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;
using static GaoApp.Tests.Reports.ProfitReportAggregationPolicyTests;

namespace GaoApp.Tests.Reports;

public sealed class ManagementReportTests
{
    [Fact]
    public void Allocation_reconciles_every_day_every_partial_range_and_final_cent()
    {
        var expense = new OperatingExpense { Amount = 100m, RecognitionFrom = new(2026, 9, 1), RecognitionTo = new(2026, 9, 30) };
        var amounts = Enumerable.Range(0, 30).Select(i => OperatingExpensePolicy.Allocate(expense, expense.RecognitionFrom.AddDays(i), expense.RecognitionFrom.AddDays(i))).ToArray();
        Assert.Equal(100m, amounts.Sum());
        Assert.Equal(amounts.Take(7).Sum(), OperatingExpensePolicy.Allocate(expense, new(2026,9,1), new(2026,9,7)));
        Assert.Equal(amounts[29], OperatingExpensePolicy.Allocate(expense, new(2026,9,30), new(2026,10,15)));
        Assert.Equal(0, OperatingExpensePolicy.Allocate(expense, new(2026,10,1), new(2026,10,15)));
    }
    [Fact]
    public void Dimensions_reconcile_returns_costs_and_historical_customer_type()
    {
        var source=Snapshot(); source.Orders[0].CustomerId=7; source.Orders[0].CustomerPriceTierSnapshot="WHOLESALE";
        source.Customers.Add(new(7,"Đổi loại khách sau bán","RETAIL"));
        source.Products.Add(new(1,"Gạo","RICE",1,"Lương thực"));
        Return(source, 2, restock:false);
        var lineCosts=new Dictionary<int,ProfitReportAggregationPolicy.CostResult>();
        var policy=Policy();var costs=policy.EvaluateCosts(source,lineCosts);
        var q=Query();var result=policy.Aggregate(source,q,costs);
        source.OperatingExpenses.Add(new OperatingExpense {StoreId=1,Status="confirmed",Amount=12.34m,RecognitionFrom=At.Date,RecognitionTo=At.Date});
        source.OperatingExpenses.Add(new OperatingExpense {StoreId=1,Status="draft",Amount=99,RecognitionFrom=At.Date,RecognitionTo=At.Date});
        source.OperatingExpenses.Add(new OperatingExpense {StoreId=1,Status="voided",Amount=99,RecognitionFrom=At.Date,RecognitionTo=At.Date});
        var period=Service().BuildPeriod(source,q,result,lineCosts);
        Assert.Equal(result.Summary.NetSales.Value,period.Products.Sum(x=>x.Summary.NetSales.Value));
        Assert.Equal(result.Summary.Cogs.Value,period.Products.Sum(x=>x.Summary.Cogs.Value));
        Assert.Equal(result.Summary.GrossProfit.Value,period.Categories.Sum(x=>x.Summary.GrossProfit.Value));
        Assert.Equal("WHOLESALE",Assert.Single(period.Customers).Group);
        Assert.Equal(0,period.InferredCustomerOrders);Assert.Equal(1,period.DraftExpenses);
        Assert.Equal(12.34m,period.OperatingExpenses);Assert.Equal(12.34m,period.ExpenseTrend.Sum());
        Assert.Equal(result.Summary.GrossProfit.Value-12.34m,period.OperatingProfit);
        Assert.Equal(1,Assert.Single(period.Products).SalesOrders);
        q.TerminalId=1;Assert.Null(Service().BuildPeriod(source,q,result,lineCosts).OperatingProfit);
    }
    [Fact]
    public void Missing_cost_is_never_shown_as_zero_in_product_or_customer_analysis()
    {
        var source=Snapshot();source.Entries.Clear();var q=Query();var policy=Policy();
        var lineCosts=new Dictionary<int,ProfitReportAggregationPolicy.CostResult>();
        var result=policy.Aggregate(source,q,policy.EvaluateCosts(source,lineCosts));
        var data=Service().BuildPeriod(source,q,result,lineCosts);
        Assert.Null(data.OperatingProfit);Assert.Null(Assert.Single(data.Products).Summary.Cogs.Value);
        Assert.Null(Assert.Single(data.CustomerGroups).Summary.GrossProfit.Value);
        Assert.Equal(200,Assert.Single(data.Products).Summary.NetSales.Value);
    }
    [Fact]
    public void Incomplete_period_revenue_is_not_presented_as_complete_dimension_totals()
    {
        var source=Snapshot(); var policy=Policy(); var q=Query();
        var lineCosts=new Dictionary<int,ProfitReportAggregationPolicy.CostResult>();
        var result=policy.Aggregate(source,q,policy.EvaluateCosts(source,lineCosts));
        result=result with {Summary=ProfitReportAggregationPolicy.Summarize(null,[new(100,0,ProfitQuality.Finalized,0)],true)};
        var data=Service().BuildPeriod(source,q,result,lineCosts);
        Assert.Null(Assert.Single(data.Products).Summary.NetSales.Value);
        Assert.Null(Assert.Single(data.CustomerGroups).Summary.GrossProfit.Value);
    }
    [Fact]
    public async Task Expense_lifecycle_enforces_tenant_version_and_idempotent_create()
    {
        var database=Guid.NewGuid().ToString();await using var db=Context(1,database);await using var foreign=Context(2,database);
        var service=new OperatingExpenseService(db);
        var request=new ExpenseWriteDto {ClientRequestId=Guid.NewGuid(),Name="Thuê cửa hàng",Amount=100,Category="rent",RecognitionFrom=At.Date,RecognitionTo=At.Date};
        var draft=await service.CreateAsync(request,default);var same=await service.CreateAsync(request,default);Assert.Equal(draft.Id,same.Id);
        request.Amount=101;await Assert.ThrowsAsync<ConflictAppException>(()=>service.CreateAsync(request,default));request.Amount=100;
        var entity=await db.OperatingExpenses.SingleAsync();entity.RowVersion=[1,2,3,4];await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictAppException>(()=>service.TransitionAsync(draft.Id,"confirm",new(){RowVersion="AA=="},default));
        var confirmed=await service.TransitionAsync(draft.Id,"confirm",new(){RowVersion=Convert.ToBase64String(entity.RowVersion)},default);
        Assert.Equal("confirmed",confirmed.Status);
        request.RowVersion=confirmed.RowVersion;await Assert.ThrowsAsync<ConflictAppException>(()=>service.UpdateAsync(draft.Id,request,default));
        var totals=await service.ListAsync(At.Date,At.Date,null,null,1,default);Assert.Equal(100,totals.ConfirmedAmount);
        var outsider=new OperatingExpenseService(foreign);Assert.Empty((await outsider.ListAsync(At.Date,At.Date,null,null,1,default)).Items);
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>outsider.TransitionAsync(draft.Id,"void",new(){RowVersion=confirmed.RowVersion,Reason="Sai cửa hàng"},default));
        await service.TransitionAsync(draft.Id,"void",new(){RowVersion=confirmed.RowVersion,Reason="Chi phí ghi trùng"},default);
        Assert.Equal(0,(await service.ListAsync(At.Date,At.Date,null,null,1,default)).ConfirmedAmount);
    }
    [Fact]
    public async Task Management_repository_reads_metadata_and_expenses_only_inside_current_store()
    {
        var database=Guid.NewGuid().ToString();await using var own=Context(1,database);await using var foreign=Context(2,database);
        foreach(var db in new[]{own,foreign}){db.OperatingExpenses.Add(new(){StoreId=db.CurrentStoreId!.Value,Name="Chi phí",Amount=9,RecognitionFrom=At.Date,RecognitionTo=At.Date});await db.SaveChangesAsync();}
        var periods=new SalesReportingPeriodPolicy().Resolve(new(){FromDate=At.Date,ToDate=At.Date,Compare="none"},At);periods.IncludeManagement=true;
        var data=await new ProfitReportReadRepository(own).ReadAsync(1,periods,false);
        Assert.Equal(1,Assert.Single(data.OperatingExpenses).StoreId);
    }
    private static ManagementReportService Service()=>new(new Reader(),new Store(),new SalesReportingPeriodPolicy(),Policy(),ProfitReportExecutionGate.Default);
    private sealed class Store:ICurrentStore{public int StoreId=>1;}
    private sealed class Reader:IProfitReportReadRepository{public Task<ProfitSourceSnapshot> ReadAsync(int storeId,SalesResolvedPeriodSet p,bool activity,CancellationToken ct=default)=>Task.FromResult(Snapshot());}
    private static InMemoryAppDbContext Context(int store,string database){var tenant=new TenantContext();tenant.SetStore(store,"test");return new(new DbContextOptionsBuilder<InMemoryAppDbContext>().UseInMemoryDatabase(database).Options,tenant,new User());}
    private sealed class User:ICurrentUser{public int? UserId=>1;public string? UserName=>"test";public int? TerminalId=>null;public string? TerminalCode=>null;public bool IsAuthenticated=>true;}
}
