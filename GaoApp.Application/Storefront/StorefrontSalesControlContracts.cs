using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;

namespace GaoApp.Application.Storefront;

public sealed class StorefrontSalesControlRequest
{
    [Required] public string Version { get; set; } = "";
    [Required, StringLength(1000)] public string Reason { get; set; } = "";
}

// Store permission only: this is not an assertion that the separate public host
// is deployed, admitted by its server policy, or approved for actual release.
public sealed record StorefrontSalesControlState(bool IsEnabled, int Revision, string Version,
    bool CanActivate, bool CanStop, IReadOnlyList<string> ActivationBlockers);

public interface IStorefrontSalesControlService
{
    Task<StorefrontSalesControlState> GetAsync(CancellationToken ct = default);
    Task<StorefrontSalesControlState> ActivateAsync(StorefrontSalesControlRequest request, CancellationToken ct = default);
    Task<StorefrontSalesControlState> StopNewOrdersAsync(StorefrontSalesControlRequest request, CancellationToken ct = default);
}

public static class StorefrontSalesControlMessages
{
    public static string Describe(string code) => code switch
    {
        "ACTIVATION_POLICY_CLOSED" => "Người vận hành chưa cho phép thao tác bật nhận đơn trên máy Admin",
        "STORE_SCOPE_INVALID" => "Cửa hàng không còn hoạt động hoặc chưa đúng phạm vi",
        "SETTINGS_MISSING" => "Cần lưu cấu hình cửa hàng trước",
        "HOST_STORE_BINDING_INVALID" => "Cần cấu hình đúng hostname và cửa hàng cố định trên server",
        "BRANDING_CONTACT_INVALID" => "Cần tên hiển thị và số điện thoại liên hệ hợp lệ",
        "SOURCE_OWNER_INVALID" => "Cần kho nguồn và chủ thể hoạt động theo chế độ cửa hàng",
        "PUBLICATION_MISSING" => "Cần ít nhất một hàng và đơn vị được đăng bán hợp lệ",
        "PUBLICATION_UNIT_PRICE_WEIGHT_INVALID" => "Kiểm tra trạng thái hàng, biến thể, đơn vị, giá và kg của các lựa chọn đang đăng",
        "SHIPPING_ZONE_RATE_INVALID" => "Kiểm tra vùng giao hoạt động và phiên bản bảng phí phù hợp chế độ kg",
        "PAYMENT_METHOD_MISSING" => "Cần ít nhất một phương thức thanh toán",
        "SHIPPING_TREATMENT_INVALID" => "Cần chính sách cửa hàng thu phí giao và ghi chi phí riêng",
        "QR_BINDING_REGISTRATION_INVALID" => "Kiểm tra cấu hình ACB, đăng ký định danh kênh, tài khoản và quyền thu QR",
        _ => "Tải lại trạng thái và kiểm tra hướng dẫn vận hành trước khi bật nhận đơn"
    };
}

public static class StorefrontHostIdentity
{
    public static string? Normalize(string? value)
    {
        var host = value?.Trim().TrimEnd('.');
        if (string.IsNullOrEmpty(host) || host.Any(char.IsControl) || host.Any(char.IsWhiteSpace) || host.IndexOfAny(['/', '\\', '@', '?', '#']) >= 0) return null;
        if (IPAddress.TryParse(host.Trim('[', ']'), out var address)) return address.ToString().ToLowerInvariant();
        try
        {
            host = new IdnMapping().GetAscii(host).ToLowerInvariant();
            return host.Length <= 253 && Uri.CheckHostName(host) == UriHostNameType.Dns ? host : null;
        }
        catch (ArgumentException) { return null; }
    }
}
