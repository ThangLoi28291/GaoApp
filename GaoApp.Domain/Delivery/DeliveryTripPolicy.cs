using System.Security.Cryptography;
using System.Text;

namespace GaoApp.Domain.Delivery;

public static class DeliveryTripPolicy
{
    public const int MaximumOrders = 50;
    public const string StaffAttestation = "Nhân viên tại tiệm xác nhận người giao đã nhận hàng.";
    public static string? NormalizeNote(string? value)
    {
        var result = value?.Trim();
        DeliveryValues.Require(result is null || result.Length <= 500, "NOTE_INVALID", "Nội dung tối đa 500 ký tự.");
        return string.IsNullOrEmpty(result) ? null : result;
    }
    public static string Reason(string? value)
    {
        var result = NormalizeNote(value);
        DeliveryValues.Require(result is not null, "REASON_REQUIRED", "Cần ghi lý do.");
        return result!;
    }
    public static byte[] Version(string? value)
    {
        byte[] bytes;
        try { bytes = Convert.FromBase64String(value ?? ""); }
        catch (FormatException) { throw new DeliveryRuleException("VERSION_INVALID", "Phiên bản không hợp lệ."); }
        DeliveryValues.Require(bytes.Length == 8 && Convert.ToBase64String(bytes) == value,
            "VERSION_INVALID", "Phiên bản không hợp lệ.");
        return bytes;
    }
    public static void Selection(IReadOnlyList<int>? orderIds)
        => DeliveryValues.Require(orderIds is { Count: >= 1 and <= MaximumOrders } &&
            orderIds.All(x => x > 0) && orderIds.Distinct().Count() == orderIds.Count,
            "ORDERS_INVALID", "Chọn từ 1 đến 50 đơn khác nhau.");
    public static void PhysicalReceipt(bool confirmed, int confirmedCourier, int expectedCourier)
        => DeliveryValues.Require(confirmed && expectedCourier > 0 && confirmedCourier == expectedCourier,
            "PHYSICAL_RECEIPT_REQUIRED", "Cần xác nhận đúng người giao đã nhận hàng thực tế.");
    public static void State(DeliveryTripState actual, DeliveryTripState expected)
        => DeliveryValues.Require(actual == expected, "TRIP_STATE_INVALID", "Trạng thái chuyến không cho phép thao tác.");
    // Stable, domain-separated child identity. Stop ordering is never used as an identity substitute.
    public static Guid ChildCommandId(Guid root, int orderId, string operation = "inventory-dispatch")
    {
        DeliveryValues.Require(root != Guid.Empty && orderId > 0 && operation == "inventory-dispatch",
            "REQUEST_INVALID", "Khóa lệnh con không hợp lệ.");
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"GaoApp:DeliveryTrip:v1:{root:D}:{orderId}:{operation}"));
        return new Guid(digest.AsSpan(0, 16));
    }
}
