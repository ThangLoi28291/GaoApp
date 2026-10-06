namespace GaoApp.Domain.Delivery;

public static class DeliveryReportPolicy
{
    public static DeliveryOutcomeReport Record(DeliveryActor actor, DeliveryOrigin origin, DeliveryState state,
        int assignedCourierId, int reportedCourierId, DeliveryReportSource source,
        DateTimeOffset departedAtUtc, DateTimeOffset serverRecordedAtUtc, DateTimeOffset? customerReceivedAtUtc = null)
    {
        DeliveryAccessPolicy.EnsureReport(actor, origin, assignedCourierId, reportedCourierId, source);
        DeliveryValues.Require(state is DeliveryState.Delivering or DeliveryState.AwaitingReconciliation,
            "REPORT_LOCKED", "Chỉ báo kết quả chuyến đang giao/chờ đối soát; không sửa sau khi khóa kết quả.");
        DeliveryValues.Utc(departedAtUtc);
        DeliveryValues.Utc(serverRecordedAtUtc);
        DeliveryValues.Require(departedAtUtc >= origin.CreatedAtUtc && serverRecordedAtUtc >= departedAtUtc,
            "TIMELINE_INVALID", "Giờ báo cáo không thể trước giờ xuất phát.");
        if (customerReceivedAtUtc.HasValue)
        {
            DeliveryValues.Utc(customerReceivedAtUtc.Value);
            DeliveryValues.Require(customerReceivedAtUtc >= departedAtUtc && customerReceivedAtUtc <= serverRecordedAtUtc,
                "CUSTOMER_TIME_INVALID", "Giờ khách nhận phải nằm trong khoảng xuất phát đến khi server ghi nhận.");
        }
        return new(actor.UserId, assignedCourierId, source, serverRecordedAtUtc, customerReceivedAtUtc);
    }

    // Display-only: no state change, GPS inference, stock receipt or payment confirmation.
    public static TimeSpan Elapsed(DateTimeOffset departedAtUtc, DateTimeOffset serverNowUtc)
    {
        DeliveryValues.Utc(departedAtUtc);
        DeliveryValues.Utc(serverNowUtc);
        DeliveryValues.Require(serverNowUtc >= departedAtUtc, "TIMELINE_INVALID", "Giờ hiện tại trước giờ xuất phát.");
        return serverNowUtc - departedAtUtc;
    }
}
