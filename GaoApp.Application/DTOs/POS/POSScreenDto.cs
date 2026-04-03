using GaoApp.Application.DTOs.POSShifts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GaoApp.Application.DTOs.POS
{
    public sealed class POSScreenDto
    {
        public CurrentCartDto CurrentCart { get; set; } = new();
        public OrderDraftDto? CurrentDraft { get; set; }

        public List<ActiveDraftOrderDto> DraftOrders { get; set; } = new();
        public List<HeldOrderDto> HeldOrders { get; set; } = new();
    }
}
