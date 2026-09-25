using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.POS;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Common.POS;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.Services.Offline;

/// <summary>Atomically commit a POS write and its retry response, after MVC authorization.</summary>
public sealed class PosOperationFilter(AppDbContext db, IPOSRuntimeContextAccessor runtime,
    IOptions<JsonOptions> jsonOptions, IAppUnitOfWork unitOfWork) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (HttpMethods.IsGet(request.Method) || !request.Headers.TryGetValue("X-POS-Operation-Id", out var rawId))
        {
            await next();
            return;
        }
        if (!Guid.TryParse(rawId, out var operationId) || operationId == Guid.Empty ||
            runtime.StoreId is not > 0 || runtime.TerminalId is not > 0 || runtime.UserId is not > 0)
            throw new ConflictAppException("Không xác định được quầy hoặc mã thao tác POS.");

        // Exclude services and cancellation tokens supplied by the framework.
        var arguments = context.ActionArguments.Where(x =>
            x.Value is not CancellationToken &&
            context.ActionDescriptor.Parameters.FirstOrDefault(p => p.Name == x.Key)?.BindingInfo?.BindingSource?.Id != "Services")
            .OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Value);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            request.Method + "\n" + request.Path + request.QueryString + "\n" + request.Headers["X-POS-Shift-Id"] + "\n" +
            request.Headers["X-POS-Expected-Order-Id"] + "\n" + JsonSerializer.Serialize(arguments, jsonOptions.Value.JsonSerializerOptions))));
        var ct = context.HttpContext.RequestAborted;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Serialize writes for this terminal as well as competing retries of the same key.
        foreach (var resource in new[] { $"pos-operation:{runtime.StoreId}:{operationId:N}", $"pos-terminal:{runtime.StoreId}:{runtime.TerminalId}" })
            await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=5000; IF @result < 0 THROW 51000, 'POS operation is busy; retry the same operation id.', 1;", ct);

        var receipt = await db.Set<PosOperationReceipt>().IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.StoreId == runtime.StoreId && x.OperationId == operationId, ct);
        if (receipt != null)
        {
            if (receipt.TerminalId != runtime.TerminalId || receipt.UserId != runtime.UserId || receipt.RequestHash != hash)
                throw new ConflictAppException("Mã thao tác POS đã được dùng cho nội dung hoặc nhân viên khác.");
            context.Result = new ContentResult { Content = receipt.ResponseJson, ContentType = "application/json; charset=utf-8", StatusCode = 200 };
            await transaction.CommitAsync(ct);
            return;
        }

        var occurred = DateTime.UtcNow;
        var offline = request.Headers["X-POS-Offline"] == "1";
        if (request.Headers.TryGetValue("X-POS-Shift-Id", out var shiftRaw))
        {
            if (!int.TryParse(shiftRaw, out var shiftId)) throw new ConflictAppException("Mã ca POS không hợp lệ.");
            var shift = await db.POSShifts.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == runtime.StoreId &&
                x.Id == shiftId && x.TerminalId == runtime.TerminalId && x.OpenedByUserId == runtime.UserId &&
                x.Status == GaoApp.Domain.Enums.POSShiftStatus.Open, ct);
            if (shift == null) throw new ConflictAppException("Ca gốc của giao dịch đã thay đổi hoặc đã đóng. Giữ đơn tại quầy để đối soát.");
            if (request.Headers.TryGetValue("X-POS-Expected-Order-Id", out var orderRaw) &&
                (!int.TryParse(orderRaw, out var expectedOrder) || shift.CurrentOrderId != expectedOrder))
                throw new ConflictAppException("Giỏ hiện tại trên server đã thay đổi. Giữ đơn offline tại quầy để đối soát.");
        }
        else if (offline) throw new ConflictAppException("Giao dịch offline phải kèm mã ca gốc.");
        if (offline && (!DateTime.TryParse(request.Headers["X-POS-Occurred-At"], CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out occurred) ||
            occurred > DateTime.UtcNow.AddMinutes(5) || occurred < DateTime.UtcNow.AddDays(-7)))
            throw new ConflictAppException("Thời điểm giao dịch offline cần đối soát trước khi đồng bộ.");

        var executed = await next();
        if (executed.Exception != null || executed.Canceled || executed.Result is not ObjectResult result ||
            (result.StatusCode ?? 200) is < 200 or >= 300)
            return; // Disposal rolls back both the business operation and its receipt.

        unitOfWork.EnsureCanCommit();
        var response = JsonSerializer.Serialize(result.Value, jsonOptions.Value.JsonSerializerOptions);
        if (request.Headers.TryGetValue("X-POS-Expected-Total", out var expectedRaw))
        {
            if (!decimal.TryParse(expectedRaw, NumberStyles.Number, CultureInfo.InvariantCulture, out var expected))
                throw new ConflictAppException("Tổng tiền offline không hợp lệ.");
            using var responseDocument = JsonDocument.Parse(response);
            var value = responseDocument.RootElement;
            if (value.TryGetProperty("draft", out var draft)) value = draft;
            else if (value.TryGetProperty("data", out var data)) value = data;
            if (!value.TryGetProperty("grandTotal", out var total) || total.GetDecimal() != expected)
                throw new ConflictAppException("Tổng tiền trên server khác phiếu đã bán offline. Đơn được giữ tại quầy để đối soát; chưa ghi thêm tiền hoặc xuất kho.");
        }
        if (offline)
        {
            if (request.Headers.TryGetValue("X-POS-Expected-Lines-Hash", out var expectedLines))
            {
                using var document = JsonDocument.Parse(response);
                var value = document.RootElement;
                if (value.TryGetProperty("draft", out var draft)) value = draft;
                else if (value.TryGetProperty("data", out var data)) value = data;
                if (!value.TryGetProperty("lines", out var lines)) throw new ConflictAppException("Thiếu danh sách hàng để đối soát offline.");
                var signature = string.Join('|', lines.EnumerateArray().Select(line =>
                {
                    var unit = line.GetProperty("sellingUnitId");
                    return line.GetProperty("variantId").GetInt32() + ":" + (unit.ValueKind == JsonValueKind.Number ? unit.GetInt32() : 0) + ":" +
                        (line.TryGetProperty("isPromotionGift", out var gift) && gift.ValueKind == JsonValueKind.True ? 1 : 0) + ":" +
                        line.GetProperty("quantity").GetDecimal().ToString("0.####", CultureInfo.InvariantCulture);
                }).OrderBy(x => x, StringComparer.Ordinal));
                var actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature)));
                if (!string.Equals(actual, expectedLines, StringComparison.OrdinalIgnoreCase))
                    throw new ConflictAppException("Hàng hoặc số lượng quà tặng trên server khác phiếu offline; cần đối soát trước khi ghi nhận.");
            }
            foreach (var order in db.ChangeTracker.Entries<Order>().Where(x => x.Entity.CompletedAtUtc >= transactionStartedUtc).Select(x => x.Entity))
                order.CompletedAtUtc = occurred;
            foreach (var qr in db.ChangeTracker.Entries<PosPaymentQrRequest>().Where(x => x.Entity.ManualConfirmedAtUtc >= transactionStartedUtc).Select(x => x.Entity))
                qr.PaidAtUtc = qr.ManualConfirmedAtUtc = occurred;
            foreach (var session in db.ChangeTracker.Entries<AcbQrSession>().Where(x => x.Entity.ConfirmationSource == GaoApp.Domain.Enums.AcbConfirmationSource.OfflineManual &&
                x.Entity.ConfirmedAtUtc >= transactionStartedUtc).Select(x => x.Entity)) session.ConfirmedAtUtc = occurred;
            foreach (var payment in db.ChangeTracker.Entries<OrderPayment>().Where(x => x.Entity.CreatedAtUtc >= transactionStartedUtc).Select(x => x.Entity))
            {
                payment.PaidAtUtc = occurred;
                payment.MetadataJson = JsonSerializer.Serialize(new { source = "offline-manual", operationId, confirmedBy = runtime.UserId, occurredAtUtc = occurred });
            }
        }
        db.Set<PosOperationReceipt>().Add(new PosOperationReceipt
        {
            StoreId = runtime.StoreId.Value, TerminalId = runtime.TerminalId.Value, UserId = runtime.UserId.Value,
            OperationId = operationId, RequestHash = hash, ResponseJson = response, WasOffline = offline, OccurredAtUtc = occurred
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await transaction.DisposeAsync();
        await unitOfWork.RunDeferredActionsAsync(ct);
    }

    private readonly DateTime transactionStartedUtc = DateTime.UtcNow;
}
