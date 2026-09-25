using System.ComponentModel.DataAnnotations;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

public sealed class StoreAcbSettings : BaseStoreEntity
{
    public bool Enabled { get; set; }
    public int BankAccountId { get; set; }
    [MaxLength(500)] public string TokenEndpoint { get; set; } = "https://sandbox.acb.com.vn/acb/open/oauth2/token";
    [MaxLength(500)] public string ApiBaseUrl { get; set; } = "https://sandbox.acb.com.vn";
    [MaxLength(500)] public string QrEndpoint { get; set; } = "https://sandbox.acb.com.vn/acb/open/payments/qr-payment/v1/initiate";
    [MaxLength(200)] public string ClientId { get; set; } = "";
    public string ClientSecretProtected { get; set; } = "";
    public string CallbackApiKeyProtected { get; set; } = "";
    [MaxLength(100)] public string TokenScope { get; set; } = "soba-api";
    [MaxLength(100)] public string XService { get; set; } = "QRPAYMENT";
    [MaxLength(100)] public string XProviderId { get; set; } = "";
    [MaxLength(100)] public string XOwnerNumber { get; set; } = "";
    [MaxLength(100)] public string XOwnerType { get; set; } = "ORG";
    [MaxLength(100)] public string VirtualAccountPrefix { get; set; } = "";
    [MaxLength(100)] public string MerchantId { get; set; } = "";
    [MaxLength(200)] public string BeneficiaryName { get; set; } = "";
}
