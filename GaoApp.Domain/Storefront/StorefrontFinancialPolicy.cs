namespace GaoApp.Domain.Storefront;

public static class StorefrontFinancialPolicy
{
    // Allocation contract only. These values do not decide VAT treatment or
    // invoke invoice/treasury posting. Tax remains with the existing owner.
    public static StorefrontFinancialSplit Split(decimal goods, decimal shippingCharge,
        decimal verifiedDeliveryCharge, StorefrontShippingFeeTreatment treatment)
    {
        StorefrontValues.Money(goods);
        StorefrontValues.Money(shippingCharge);
        StorefrontValues.Money(verifiedDeliveryCharge);
        StorefrontValues.Require(goods > 0 && Enum.IsDefined(treatment) && treatment != StorefrontShippingFeeTreatment.Unconfigured,
            "SHIPPING_ACCOUNTING_UNCONFIGURED", "Cần chốt cách ghi nhận phí giao hàng trước khi mở bán.");
        var total = goods + shippingCharge;
        StorefrontValues.Money(total);
        var passThrough = treatment == StorefrontShippingFeeTreatment.CarrierPassThrough;
        var difference = passThrough ? shippingCharge - verifiedDeliveryCharge : 0;
        return new(goods,
            passThrough ? 0 : shippingCharge,
            passThrough ? shippingCharge : 0,
            total, passThrough ? 0 : verifiedDeliveryCharge, 0,
            passThrough ? verifiedDeliveryCharge : 0, difference, difference != 0);
    }

    // A carrier may remit net of a fee. The deduction is supplied only from an
    // authorised, evidenced reconciliation, never from a customer/driver DTO.
    public static StorefrontCodPosition Cod(decimal totalDue, decimal customerCollected, decimal storeRemitted,
        decimal verifiedCarrierDeduction = 0)
    {
        StorefrontValues.Money(totalDue);
        StorefrontValues.Money(customerCollected);
        StorefrontValues.Money(storeRemitted);
        StorefrontValues.Money(verifiedCarrierDeduction);
        StorefrontValues.Require(totalDue > 0 && customerCollected <= totalDue && storeRemitted <= customerCollected &&
            verifiedCarrierDeduction <= customerCollected - storeRemitted,
            "COD_BUDGET_INVALID", "Khoản COD thu/bàn giao vượt ngân sách đã đối soát.");
        return new(customerCollected, storeRemitted, customerCollected - storeRemitted - verifiedCarrierDeduction,
            customerCollected == totalDue, storeRemitted + verifiedCarrierDeduction == totalDue, verifiedCarrierDeduction);
    }

    // Confirm only an actually executed refund, not an outgoing-bank command.
    public static decimal RefundBudget(decimal customerPaid, decimal alreadyRefunded,
        decimal proposedRefund, decimal verifiedExecutedAmount)
    {
        foreach (var amount in new[] { customerPaid, alreadyRefunded, proposedRefund, verifiedExecutedAmount })
            StorefrontValues.Money(amount);
        StorefrontValues.Require(proposedRefund > 0 && alreadyRefunded <= customerPaid &&
            proposedRefund <= customerPaid - alreadyRefunded && verifiedExecutedAmount == proposedRefund,
            "REFUND_BUDGET_INVALID", "Hoàn tiền phải có bằng chứng thực hiện và không vượt khoản còn có thể hoàn.");
        return alreadyRefunded + proposedRefund;
    }
}
