using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services.Acb;

public sealed class AcbFinalizeGuard(AppDbContext db) : IOrderFinalizeGuard
{
    public async Task ValidateAsync(Order order, CancellationToken ct)
    {
        var sessions = await db.Set<AcbQrSession>().Where(x => x.StoreId == order.StoreId && x.OrderId == order.Id &&
            x.Status != AcbSessionStatus.Cancelled).ToListAsync(ct);
        foreach (var session in sessions)
        {
            var payment = order.Payments.SingleOrDefault(x => x.Id == session.PaymentId && !x.IsDeleted);
            if (session.Status != AcbSessionStatus.Received || payment == null || payment.Amount != session.Amount ||
                payment.ReferenceCode != session.ProviderOrderId || session.ShiftId != order.POSShiftId ||
                !AcbPaymentPolicy.MatchesFingerprint(order, session.CartFingerprint))
                throw new BusinessRuleException("QR ACB chưa được xác nhận hợp lệ hoặc đơn đã thay đổi. Hãy kiểm tra giao dịch trước khi chốt.");
        }
    }
}
