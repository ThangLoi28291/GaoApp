using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GaoApp.Application.DTOs.POS
{
    public sealed class ActiveDraftOrderDto
    {
        public int OrderId { get; set; }
        public string? OrderNumber { get; set; }
        public string? Note { get; set; }

        public int LineCount { get; set; }
        public decimal TotalQuantity { get; set; }
        public decimal GrandTotal { get; set; }

        public DateTime CreatedAtUtc { get; set; }
        public bool IsCurrent { get; set; }
    }
}
