using GaoApp.Application.Storefront;

namespace GaoApp.Web.Areas.Admin.Models;

public sealed record StorefrontPayerBankOption(int Id, string Name, string Version);

public sealed record StorefrontRefundPage(StorefrontCancellationRead Cancellation, IReadOnlyList<StorefrontRefundRead> Refunds,
    IReadOnlyList<StorefrontOption> PayerLegalEntities, IReadOnlyList<StorefrontPayerBankOption> PayerBankAccounts,
    bool CanApproveRefund, bool CanConfirmRefund, int StoreId, int ActorUserId);
