using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GaoApp.Application.DTOs.POSShifts
{
    public sealed class CurrentCartDto
    {
        public int? CurrentOrderId { get; set; }
        public bool HasCurrentOrder => CurrentOrderId.HasValue;
    }
}
