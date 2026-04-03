using GaoApp.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GaoApp.Application.DTOs.POS
{
    public sealed class QuickAddPaymentRequest
    {
        public PaymentMethod Method { get; set; }
        public decimal Amount { get; set; }
        public string? ReferenceCode { get; set; }
        public string? Provider { get; set; }
    }
}
