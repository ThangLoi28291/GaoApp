using GaoApp.Application.Storefront;
using Microsoft.Extensions.Configuration;

namespace GaoApp.Infrastructure.Services.Storefront;

public sealed class StorefrontBankRegistration(IConfiguration configuration) : IStorefrontBankRegistration
{
    public GaoApp.Application.Storefront.StorefrontBankRegistration? Get(int storeId)
    {
        var section=configuration.GetSection("StorefrontBanking:Registrations:"+storeId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if(!bool.TryParse(section["NamespaceVerified"],out var verified)||!verified||section["Namespace"]!="W"||section["Provider"]!="ACB")return null;
        if(!int.TryParse(section["BankAccountId"],out var bankId)||!int.TryParse(section["ReceivingLegalEntityId"],out var ownerId))return null;
        var evidence=section["EvidenceReference"]?.Trim(); var merchant=section["MerchantId"]?.Trim();
        var terminal=section["TerminalCode"]?.Trim(); var user=section["UserCode"]?.Trim(); var account=section["AccountIdentity"]?.Trim();
        if(string.IsNullOrWhiteSpace(evidence)||evidence.Length>200||string.IsNullOrWhiteSpace(merchant)||merchant.Length>30||
            string.IsNullOrWhiteSpace(terminal)||terminal.Length>100||string.IsNullOrWhiteSpace(user)||user.Length>100||
            account is null||!System.Text.RegularExpressions.Regex.IsMatch(account,"^[A-Fa-f0-9]{64}$")||
            bankId<=0||ownerId<=0) return null;
        var mappingEvidence=section["NotificationIdentityMappingEvidence"]?.Trim();
        var mappingVerified=bool.TryParse(section["NotificationIdentityMappingVerified"],out var mapping)&&mapping&&!string.IsNullOrWhiteSpace(mappingEvidence)&&mappingEvidence.Length<=200;
        return new(true,evidence,merchant,terminal,user,bankId,ownerId,account.ToUpperInvariant(),mappingVerified,mappingVerified?mappingEvidence:null);
    }
}
