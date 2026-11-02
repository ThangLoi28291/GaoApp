using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Services.Banking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GaoApp.Infrastructure.Services.Storefront;

public sealed partial class StorefrontCheckoutService
{
    internal static string ProviderFingerprint(StoreAcbSettings value) => Hash(Serialize(new {value.BankAccountId,value.ClientId,
        value.TokenEndpoint,value.ApiBaseUrl,value.QrEndpoint,value.MerchantId,value.BeneficiaryName,value.VirtualAccountPrefix,
        value.XOwnerNumber,value.XOwnerType,value.XProviderId,value.XService}));
    private async Task<StorefrontBankBinding> BankBinding(StorefrontSettings settings, IReadOnlyList<Source> sources,
        IReadOnlyList<StorefrontCheckoutLine> lines, decimal total, IReadOnlyList<InventoryBalance> inventory, CancellationToken ct)
    {
        var registration=bankRegistration?.Get(Store);
        Require(registration?.Verified==true, "QR chưa có xác nhận đăng ký kênh website tại ngân hàng. Bạn có thể chủ động chọn COD nếu cửa hàng hỗ trợ.");
        Require(total>0&&total<=int.MaxValue&&decimal.Truncate(total)==total,"Tổng phải thu vượt giới hạn QR ACB. Hãy chọn phương thức khác hoặc điều chỉnh giỏ.");
        Require(settings.QrHoldMinutes is >0 and <=10080,"Chưa cấu hình thời hạn QR hợp lệ.");
        var variants=lines.Select(x=>x.VariantId).Distinct().ToList();
        Require(await db.ProductVariants.AsNoTracking().CountAsync(x=>x.StoreId==Store&&variants.Contains(x.Id)&&x.HasInputInvoice,ct)==variants.Count,
            "QR chưa khả dụng cho một số hàng trong giỏ. Cửa hàng cần kiểm tra điều kiện thanh toán; bạn có thể chọn COD.");
        var acb=await db.Set<StoreAcbSettings>().AsNoTracking().SingleOrDefaultAsync(x=>x.StoreId==Store&&x.Enabled,ct);
        var bank=await db.StoreBankAccounts.AsNoTracking().SingleOrDefaultAsync(x=>x.StoreId==Store&&x.Id==settings.BankAccountId&&x.IsActive,ct);
        Require(acb is not null&&bank is not null&&bank.BankCode.Trim().Equals("ACB",StringComparison.OrdinalIgnoreCase)&&acb.BankAccountId==bank.Id,
            "Cấu hình tài khoản nhận QR chưa khớp.");
        _=AcbProtocolCore.ValidateEnvironment(acb!.TokenEndpoint,acb.ApiBaseUrl,acb.QrEndpoint);
        Require(!string.IsNullOrWhiteSpace(acb.ClientId)&&!string.IsNullOrWhiteSpace(acb.ClientSecretProtected)&&!string.IsNullOrWhiteSpace(acb.CallbackApiKeyProtected)&&
            acb.MerchantId.Length is >0 and <=30&&acb.BeneficiaryName.Length is >0 and <=30,"Cấu hình ACB chưa sẵn sàng cho QR.");
        Require(await db.LegalEntities.AsNoTracking().AnyAsync(x=>x.StoreId==Store&&x.Id==settings.ReceivingLegalEntityId&&x.IsActive,ct),"Chủ thể nhận QR chưa sẵn sàng.");
        var accountIdentity=GlobalBankReceiptLedger.AccountIdentity(bank!.BankCode,bank.AccountNumber);
        var phoneExposure=await db.Set<BankNotificationDevice>().IgnoreQueryFilters().AsNoTracking().AnyAsync(device=>device.BankAccountFingerprint==accountIdentity&&
            (device.Enabled||db.Set<BankNotificationReceipt>().IgnoreQueryFilters().Any(receipt=>receipt.DeviceId==device.Id)),ct);
        Require(!phoneExposure||registration!.NotificationIdentityMappingVerified,
            "Tài khoản này chưa có xác minh định danh dùng chung với kênh thông báo ngân hàng. QR website chưa sẵn sàng; hãy chọn COD hoặc tài khoản nhận riêng.");
        Require(registration!.BankAccountId==bank.Id&&registration.ReceivingLegalEntityId==settings.ReceivingLegalEntityId&&registration.AccountIdentity==accountIdentity&&
            registration.MerchantId==acb.MerchantId&&registration.TerminalCode==settings.BankTerminalCode&&registration.UserCode==settings.BankUserCode,
            "Thông tin đăng ký kênh QR không khớp tài khoản, chủ thể hoặc mã kênh hiện tại.");
        // Quote-only correlation1 is input to the pure allocator, never a persisted/legacy order identity.
        var plan=AllocationPlan(lines,sources,inventory,1,settings.AllowNegativeInventory);
        Require(plan.IsSuccess,"Chưa phân bổ được nguồn hàng cho báo giá QR.");
        var owners=plan.Allocations.Select(x=>x.LegalEntityId).Distinct().Order().ToList();
        var authorities=await db.Set<StorefrontBankCollectionAuthority>().AsNoTracking().Where(x=>x.StoreId==Store&&x.IsActive&&x.BankAccountId==bank.Id&&
            x.ReceivingLegalEntityId==settings.ReceivingLegalEntityId&&owners.Contains(x.SourceLegalEntityId)).OrderBy(x=>x.SourceLegalEntityId).ToListAsync(ct);
        Require(authorities.Count==owners.Count&&owners.All(id=>authorities.Any(x=>x.SourceLegalEntityId==id)),"Thiếu quyền thu QR cho một chủ thể nguồn hàng.");
        StorefrontServiceCollectionProof? feeProof = null;
        if (total > lines.Sum(x => x.NetAmount))
        {
            var fee = await db.Set<StorefrontServiceRevenueSetting>().AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store, ct);
            if (fee is not null)
            {
                var tax = await db.Taxes.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == fee.TaxId && x.IsActive, ct);
                var authority = await db.Set<StorefrontBankCollectionAuthority>().AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.IsActive &&
                    x.BankAccountId == bank.Id && x.ReceivingLegalEntityId == settings.ReceivingLegalEntityId && x.SourceLegalEntityId == fee.LegalEntityId, ct);
                Require(tax is not null && authority is not null && await db.LegalEntities.AnyAsync(x => x.StoreId == Store && x.Id == fee.LegalEntityId && x.IsActive, ct),
                    "Thiếu thuế, chủ thể hoặc quyền thu cho phí dịch vụ giao hàng. Cửa hàng cần kiểm tra cấu hình trước khi nhận QR.");
                feeProof = new(fee.LegalEntityId, fee.TaxId, fee.Name, tax!.Rate, fee.InvoiceRoute, Convert.ToBase64String(fee.RowVersion), authority!.Id, Convert.ToBase64String(authority.RowVersion));
            }
        }
        return new(bank.Id,settings.ReceivingLegalEntityId!.Value,accountIdentity,bank.BankCode,bank.BankName,bank.AccountNumber,bank.AccountName,
            acb.MerchantId,acb.BeneficiaryName,settings.BankTerminalCode,settings.BankUserCode,acb.VirtualAccountPrefix,ProviderFingerprint(acb),
            registration.EvidenceReference,settings.QrHoldMinutes!.Value,owners,authorities.Select(x=>x.Id+":"+Convert.ToBase64String(x.RowVersion)).ToList(),
            registration.NotificationIdentityMappingVerified,registration.NotificationIdentityMappingEvidence, feeProof);
    }
    private async Task<int> CreateQrIntent(StorefrontOrder order, Data data, CancellationToken ct)
    {
        var binding=data.BankBinding??throw new InvalidOperationException("QR placement requires its sealed binding.");
        Require(db.Database.IsSqlServer(),"QR cần registry SQL dùng chung trước khi nhận đơn.");
        await using var command=db.Database.GetDbConnection().CreateCommand(); command.Transaction=db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText="SELECT NEXT VALUE FOR dbo.StorefrontBankCorrelationSequence;";
        long sequence;
        try{sequence=Convert.ToInt64(await command.ExecuteScalarAsync(ct),System.Globalization.CultureInfo.InvariantCulture);}
        catch(Microsoft.Data.SqlClient.SqlException error) when(error.Number==11728)
        {throw new GaoApp.Application.Common.Exceptions.ValidationAppException("Namespace QR ngân hàng đã hết. Không thể tạo mã mới; hãy chủ động chọn phương thức khác.");}
        Require(sequence is >0 and <=999999999999L,"Namespace ngân hàng đã hết. Không thể tạo QR mới.");
        var reference="W"+sequence.ToString("D12",System.Globalization.CultureInfo.InvariantCulture);
        var intent=new StorefrontPaymentIntent {StoreId=Store,StorefrontOrderId=order.Id,ProviderReference=reference,BankAccountId=binding.BankAccountId,
            ReceivingLegalEntityId=binding.ReceivingLegalEntityId,AmountDue=order.TotalDue,ExpiresAtUtc=Now.AddMinutes(binding.HoldMinutes),
            BankTerminalCode=binding.BankTerminalCode,BankUserCode=binding.BankUserCode,BindingSnapshotJson=Serialize(binding)};
        order.PaymentExpiresAtUtc=intent.ExpiresAtUtc;
        db.Add(intent); await db.SaveChangesAsync(ct);
        db.Add(new BankPaymentReference {Reference=reference,Channel=BankPaymentChannel.Storefront,OriginStoreId=Store,StorefrontPaymentIntentId=intent.Id,
            AccountIdentity=binding.AccountIdentity,NotificationIdentityMappingVerified=binding.NotificationIdentityMappingVerified});
        db.Add(new StorefrontQrProcessing {StoreId=Store,PaymentIntentId=intent.Id,State=(int)StorefrontQrState.Queued,TraceNumber=Guid.NewGuid().ToString(),NextAttemptAtUtc=Now});
        await db.SaveChangesAsync(ct);
        return intent.Id;
    }
}
