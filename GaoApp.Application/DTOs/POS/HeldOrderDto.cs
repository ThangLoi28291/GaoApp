using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GaoApp.Application.DTOs.POS
{
    public class HeldOrderDto
    {
        public int OrderId { get; set; }
        public string? HoldCode { get; set; }
        public string? HoldNote { get; set; }
        public DateTime? HeldAtUtc { get; set; }

        public int LineCount { get; set; }
        public decimal TotalQuantity { get; set; }
        public decimal Subtotal { get; set; }
    }
}
