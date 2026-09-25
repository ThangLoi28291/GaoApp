using System.ComponentModel.DataAnnotations;

namespace GaoApp.Web.Services.Acb;

public sealed class AcbSettingsForm
{
    public bool Enabled { get; set; }
    [Required(ErrorMessage = "Hãy chọn tài khoản nhận tiền ACB của cửa hàng.")]
    [Range(1, int.MaxValue, ErrorMessage = "Hãy chọn tài khoản nhận tiền ACB hợp lệ.")]
    public int? BankAccountId { get; set; }
    [Required, MaxLength(500)] public string TokenEndpoint { get; set; } = "https://sandbox.acb.com.vn/acb/open/oauth2/token";
    [Required, MaxLength(500)] public string ApiBaseUrl { get; set; } = "https://sandbox.acb.com.vn";
    [Required, MaxLength(500)] public string QrEndpoint { get; set; } = "https://sandbox.acb.com.vn/acb/open/payments/qr-payment/v1/initiate";
    [MaxLength(200)] public string ClientId { get; set; } = "";
    [MaxLength(500)] public string? ClientSecret { get; set; }
    [MaxLength(500)] public string? CallbackApiKey { get; set; }
    public bool HasClientSecret { get; set; }
    public bool HasCallbackApiKey { get; set; }
    [MaxLength(100)] public string TokenScope { get; set; } = "soba-api";
    [MaxLength(100)] public string XService { get; set; } = "QRPAYMENT";
    [MaxLength(100)] public string XProviderId { get; set; } = "";
    [MaxLength(100)] public string XOwnerNumber { get; set; } = "";
    [MaxLength(100)] public string XOwnerType { get; set; } = "ORG";
    [MaxLength(100)] public string VirtualAccountPrefix { get; set; } = "";
    [MaxLength(30)] public string MerchantId { get; set; } = "";
    [MaxLength(30)] public string BeneficiaryName { get; set; } = "";

    public static readonly string[] TextFields = [nameof(TokenEndpoint), nameof(ApiBaseUrl), nameof(QrEndpoint),
        nameof(ClientId), nameof(TokenScope), nameof(XService), nameof(XProviderId), nameof(XOwnerNumber),
        nameof(XOwnerType), nameof(VirtualAccountPrefix), nameof(MerchantId), nameof(BeneficiaryName)];
}
