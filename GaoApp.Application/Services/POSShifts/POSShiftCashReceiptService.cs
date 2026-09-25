using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.POSShifts;
using GaoApp.Application.Interfaces.Repositories.Orders;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.POSShifts;

public sealed class POSShiftCashReceiptService(
    IPOSShiftRepository shifts, ICurrentStore store, ICurrentUser user, IStoreAdminAccess admin)
{
    public async Task ConfirmAsync(int shiftId, ConfirmShiftCashReceiptRequest request, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        var shift = await shifts.GetByIdAsync(shiftId, ct);
        if (shift == null || shift.StoreId != store.StoreId || shift.IsDeleted)
            throw new NotFoundAppException("Không tìm thấy ca POS.");
        if (shift.Status != POSShiftStatus.Closed || !shift.ClosingCashActual.HasValue)
            throw new ConflictAppException("Nhân viên phải chốt ca trước khi admin xác nhận nhận tiền.");

        var amount = request.ReceivedAmount;
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (!amount.HasValue || amount < 0 || amount > 9999999999999999m || decimal.Truncate(amount.Value) != amount)
            throw new ValidationAppException("Nhập số tiền thực nhận không âm, làm tròn đến đồng.");
        if (note?.Length > 500)
            throw new ValidationAppException("Ghi chú tối đa 500 ký tự.");

        // A repeated submission is harmless; a confirmed receipt cannot be overwritten.
        if (shift.CashReceivedAtUtc.HasValue)
        {
            if (shift.CashReceivedAmount == amount && shift.CashReceiptNote == note)
                return;
            throw new ConflictAppException("Ca đã được xác nhận nhận tiền. Không thể ghi đè số tiền bàn giao.");
        }
        if ((amount != shift.ClosingCashActual || amount != shift.ClosingCashExpected) && note == null)
            throw new ValidationAppException("Có chênh lệch tiền. Vui lòng ghi rõ lý do trước khi xác nhận.");

        shift.CashReceivedAmount = amount;
        shift.CashReceivedByUserId = user.UserId;
        shift.CashReceivedAtUtc = DateTime.UtcNow;
        shift.CashReceiptNote = note;
        // POSShift has a rowversion: simultaneous confirmations cannot overwrite each other.
        await shifts.SaveChangesAsync(ct);
    }
}
