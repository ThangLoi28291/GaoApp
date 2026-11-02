using GaoApp.Application.Storefront;

namespace GaoApp.Web.Areas.Admin.Models;

public sealed record StorefrontFulfillmentPage(StorefrontFulfillmentDetail Detail, StorefrontGuestOrder Snapshot,
    StorefrontSettlementSummary Money, bool CanPrepare, bool CanDispatch, bool CanDelivery, bool CanReturn, bool CanReconcile, bool CanPostSale,
    IReadOnlyList<StorefrontOption> Couriers, IReadOnlyList<StorefrontOption> LegalEntities,
    IReadOnlyList<StorefrontOption> CashReceipts, IReadOnlyList<StorefrontOption> Expenses,
    IReadOnlyList<StorefrontCodBankCandidate> BankReceipts, IReadOnlyList<StorefrontOption> Notifications,
    int? OpenIncidentId);
public sealed record StorefrontServiceSettingPage(StorefrontServiceRevenueSettingDto? Setting, IReadOnlyList<StorefrontOption> LegalEntities, IReadOnlyList<StorefrontOption> Taxes, bool CanManage);
