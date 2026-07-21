using GaoApp.Application.DTOs.Invoices;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Services.Invoices;

internal static class ViettelPaymentInfoBuilder
{
    public static List<ViettelPaymentDto> Build(InvoiceProviderSetting setting)
    {
        return new List<ViettelPaymentDto>
        {
            new ViettelPaymentDto
            {
                PaymentMethodName = string.IsNullOrWhiteSpace(setting.PaymentMethodName)
                    ? "TM/CK"
                    : setting.PaymentMethodName.Trim()
            }
        };
    }
}