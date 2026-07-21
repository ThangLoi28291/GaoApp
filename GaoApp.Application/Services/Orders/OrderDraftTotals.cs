namespace GaoApp.Application.Services.Orders;

public static class OrderDraftTotals
{
    public static void ApplyPaymentTotals(decimal grandTotal, IEnumerable<decimal> paymentAmounts,
        out decimal paidTotal, out decimal balanceDue, out decimal changeDue)
    {
        paidTotal = paymentAmounts.Sum();
        balanceDue = grandTotal - paidTotal;
        if (balanceDue < 0) balanceDue = 0;

        changeDue = paidTotal - grandTotal;
        if (changeDue < 0) changeDue = 0;
    }
}