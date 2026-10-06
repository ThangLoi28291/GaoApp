namespace GaoApp.Domain.Delivery;

public static class DeliverySettlementPolicy
{
    public static DeliverySettlementPlan Plan(DeliveryOrigin origin, DeliveryClosingCounter counter,
        DeliveryActor actor, DeliveryState state, IReadOnlyList<DeliveryQuantityLine> quantities,
        IReadOnlyList<DeliveryApprovedPriceLine> prices, DeliverySettlementRequest request,
        DateTimeOffset dispatchedAtUtc, DateTimeOffset reconciledAtUtc, DateTimeOffset settledAtUtc)
    {
        DeliveryAccessPolicy.Ensure(actor, origin, DeliveryCapability.Finalize);
        DeliveryValues.Require(state == DeliveryState.ReadyToSettle, "SETTLEMENT_NOT_READY", "Chỉ chốt sau khi đối soát hàng/tiền.");
        DeliveryValues.Require(counter.StoreId == origin.StoreId && counter.TerminalId > 0 && counter.ShiftId > 0 && counter.WarehouseId > 0 &&
            counter.IsOpen && counter.ShiftOwnerUserId == actor.UserId,
            "CLOSING_COUNTER_FORBIDDEN", "Cần ca đang mở của người chốt tại cùng cửa hàng.");
        DeliveryValues.Utc(dispatchedAtUtc);
        DeliveryValues.Utc(reconciledAtUtc);
        DeliveryValues.Utc(settledAtUtc);
        DeliveryValues.Require(dispatchedAtUtc >= origin.CreatedAtUtc && reconciledAtUtc >= dispatchedAtUtc && settledAtUtc >= reconciledAtUtc,
            "TIMELINE_INVALID", "Thời điểm tạo/xuất/chốt không đúng thứ tự.");
        DeliveryValues.Require(quantities.Count > 0 && quantities.Select(x => x.LineId).Distinct().Count() == quantities.Count &&
            quantities.Count == prices.Count && quantities.All(q => prices.Any(p => p.LineId == q.LineId && p.OrderedQuantity == q.Ordered)),
            "SETTLEMENT_LINES_MISMATCH", "Số lượng và snapshot giá không cùng bộ dòng.");
        var plans = quantities.Select(DeliveryQuantityPolicy.Evaluate).ToList();
        DeliveryValues.Require(plans.All(x => x.GoodsReconciled), "GOODS_UNRECONCILED", "Hàng mang về/hư/mất phải được xác nhận, không lấy báo cáo làm nhập kho.");
        var charges = DeliveryPricingPolicy.Charge(prices, quantities.ToDictionary(x => x.LineId, x => x.Delivered));
        var amount = charges.Sum(x => x.Amount);
        DeliveryValues.Money(amount, "Tiền thực giao");
        var createsSale = quantities.Any(x => x.Delivered > 0);
        DeliveryValues.Require(!createsSale || amount > 0m, "ZERO_PRICE_UNSUPPORTED", "Đợt đầu chưa hỗ trợ bán giao hàng có tổng tiền bằng 0.");
        foreach (var value in new[] { request.ReportedCustomerCash, request.ReceivedCash, request.ConfirmedBankAmount, request.CreditAmount, request.AdvanceAmount })
            DeliveryValues.Money(value, "Đối soát tiền");
        DeliveryValues.Require(Enum.IsDefined(request.Method), "PAYMENT_METHOD_INVALID", "Phương thức chốt không hợp lệ.");
        DeliveryValues.Require(request.AdvanceAmount == 0m, "ADVANCE_UNSUPPORTED", "Đợt đầu chưa hỗ trợ đặt cọc/trả trước.");
        var methodCount = (request.ReportedCustomerCash > 0 || request.ReceivedCash > 0 ? 1 : 0) +
            (request.ConfirmedBankAmount > 0 ? 1 : 0) + (request.CreditAmount > 0 ? 1 : 0);
        DeliveryValues.Require(methodCount <= 1, "MIXED_PAYMENT_UNSUPPORTED", "Đợt đầu mỗi đơn chọn một phương thức chốt.");

        switch (request.Method)
        {
            case DeliverySettlementMethod.Cash:
                DeliveryValues.Require(request.ReportedCustomerCash == request.ReceivedCash,
                    "CASH_HANDOVER_DIFFERENCE", "Tiền khách đã trả và tiền tiệm nhận lệch nhau: cần xử lý, không chuyển thành nợ khách.");
                DeliveryValues.Require(request.ReceivedCash == amount && request.ConfirmedBankAmount == 0 && request.CreditAmount == 0,
                    "PAYMENT_AMOUNT_MISMATCH", "Tiền thực nhận phải đúng tiền hàng thực giao.");
                DeliveryAccessPolicy.Ensure(actor, origin, DeliveryCapability.ReconcileCash);
                break;
            case DeliverySettlementMethod.ConfirmedBankTransfer:
                DeliveryAccessPolicy.Ensure(actor, origin, DeliveryCapability.ConfirmBankTransfer);
                DeliveryValues.Require(request.ReportedCustomerCash == 0 && request.ReceivedCash == 0 && request.CreditAmount == 0 &&
                    request.ConfirmedBankAmount == amount && request.BankVerified && request.BankAccountStoreId == origin.StoreId &&
                    !string.IsNullOrWhiteSpace(request.BankReference) && request.BankReference.Length <= 100,
                    "BANK_UNCONFIRMED", "Phải xác nhận tiền đã vào tài khoản cùng cửa hàng và lưu tham chiếu; không tạo QR mới.");
                break;
            case DeliverySettlementMethod.CustomerCredit:
                DeliveryAccessPolicy.Ensure(actor, origin, DeliveryCapability.Credit);
                DeliveryValues.Require(request.ReportedCustomerCash == 0 && request.ReceivedCash == 0 && request.ConfirmedBankAmount == 0 &&
                    request.CreditAmount == amount && amount > 0 && request.Customer is { CustomerId: > 0, IsActive: true, HaveDebt: true } &&
                    request.Customer.StoreId == origin.StoreId && request.Customer.CustomerId == origin.CustomerId,
                    "CUSTOMER_CREDIT_FORBIDDEN", "Chỉ ghi nợ tiền thực giao cho khách đang hoạt động được phép công nợ cùng cửa hàng.");
                break;
        }
        return new(amount, request.ReceivedCash, request.ConfirmedBankAmount, request.CreditAmount, createsSale,
            counter.TerminalId, counter.ShiftId, counter.WarehouseId, origin.SourceWarehouseId, origin.SourceLegalEntityId,
            origin.CreatedTerminalId, origin.CreatedShiftId, DeliveryValues.BusinessDate(origin.CreatedAtUtc), DeliveryValues.BusinessDate(settledAtUtc),
            DeliveryValues.BusinessDate(dispatchedAtUtc), DeliveryValues.BusinessDate(reconciledAtUtc));
    }
}
