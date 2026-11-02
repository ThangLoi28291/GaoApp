using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Tests.Security;

namespace GaoApp.Tests.Invoices;

public sealed partial class AutoInvoiceServiceTests
{
    [Theory]
    [InlineData("TIMEOUT",AutoInvoiceOperationStatus.Unknown)]
    [InlineData("HTTP_500",AutoInvoiceOperationStatus.Unknown)]
    [InlineData("UNKNOWN_RESULT",AutoInvoiceOperationStatus.Unknown)]
    [InlineData("Invoice.UnitMissing",AutoInvoiceOperationStatus.Blocked)]
    [InlineData("B7_SYNTHETIC_SECRET_8c2d9741",AutoInvoiceOperationStatus.Blocked)]
    public async Task B7_dashboard_hides_queue_history_and_worker_diagnostics_without_rewriting_business_inputs(
        string code,AutoInvoiceOperationStatus status)
    {
        var now=new DateTime(2026,10,24,3,0,0,DateTimeKind.Utc);
        var repo=CreateRepository(enabled:true);
        var invoice=Invoice(7001,now.AddHours(-1));
        invoice.LastErrorCode=code;
        invoice.LastErrorMessage="timeout password="+InvoiceErrorReadSanitizerTests.Canary;
        invoice.ProviderStatus=InvoiceProviderStatus.IssueFailed;
        invoice.TransactionUuid=Guid.NewGuid().ToString();
        repo.Candidates.Add(invoice);
        var operation=new AutoInvoiceOperation {Id=81,StoreId=1,InvoiceHeadId=invoice.Id,Status=status,
            ErrorCode=code,ErrorMessage=InvoiceErrorReadSanitizerTests.Canary,
            Sources=[new AutoInvoiceOperationSource {StoreId=1,InvoiceHeadId=invoice.Id,IsActive=true}]};
        repo.Operations.Add(operation);
        repo.WorkerStates.Add(new AutoInvoiceWorkerState {StoreId=1,LastErrorCode=code,
            LastErrorMessage=InvoiceErrorReadSanitizerTests.Canary,LastResult=InvoiceErrorReadSanitizerTests.Canary});
        var issue=new FakeIssueService();
        var service=CreateService(repo,issue,nowUtc:now);
        var dashboard=await service.GetDashboardAsync(new AutoInvoiceDashboardQueryDto {Workspace="errors"});
        var row=Assert.Single(dashboard.Queue);
        Assert.True(row.HasIncident);Assert.True(row.IsUnknown);Assert.False(row.IsReady);Assert.False(row.CanRecheck);
        Assert.NotEmpty(dashboard.History);
        Assert.DoesNotContain(InvoiceErrorReadSanitizerTests.Canary,JsonSerializer.Serialize(dashboard));
        Assert.Equal(code,invoice.LastErrorCode);
        Assert.Equal("timeout password="+InvoiceErrorReadSanitizerTests.Canary,invoice.LastErrorMessage);
        Assert.Equal(InvoiceErrorReadSanitizerTests.Canary,operation.ErrorMessage);
        Assert.Equal(InvoiceErrorReadSanitizerTests.Canary,repo.WorkerStates[0].LastResult);
        Assert.Equal(status,operation.Status);Assert.Equal(0,issue.Calls);
    }
}
