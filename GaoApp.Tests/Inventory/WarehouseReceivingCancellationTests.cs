using System.Reflection;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Web.Areas.Admin.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Tests.Inventory;

public sealed class WarehouseReceivingCancellationTests
{
    [Fact]
    public async Task Disconnected_client_cancels_receipt_read_without_a_server_error()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var controller = Controller(new TaskCanceledException());
        controller.HttpContext.RequestAborted = cancelled.Token;
        var response = Assert.IsType<StatusCodeResult>(await controller.GetReceipts(cancelled.Token));
        Assert.Equal(499, response.StatusCode);
    }

    [Fact]
    public async Task Unexpected_query_cancellation_is_not_hidden_as_a_client_disconnect()
    {
        var controller = Controller(new TaskCanceledException());
        await Assert.ThrowsAsync<TaskCanceledException>(() => controller.GetReceipts(CancellationToken.None));
    }

    [Fact]
    public async Task Database_failures_remain_visible_even_if_the_client_disconnected()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var controller = Controller(new InvalidOperationException("query failed"));
        controller.HttpContext.RequestAborted = cancelled.Token;
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.GetReceipts(cancelled.Token));
    }

    private static WarehouseReceivingController Controller(Exception error)
    {
        var service = DispatchProxy.Create<IStockDocumentService, FailedRead>();
        ((FailedRead)(object)service).Error = error;
        return new WarehouseReceivingController(service, null!, null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    public class FailedRead : DispatchProxy
    {
        public Exception Error { get; set; } = new TaskCanceledException();
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Assert.Equal(nameof(IStockDocumentService.GetReceivingReceiptListAsync), targetMethod?.Name);
            return Task.FromException<List<GaoApp.Application.DTOs.Inventory.StockDocumentListItemDto>>(Error);
        }
    }
}
