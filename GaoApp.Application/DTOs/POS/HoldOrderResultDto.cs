using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GaoApp.Application.DTOs.POS
{
    public class HoldOrderResultDto
    {
        public int HeldOrderId { get; set; }
        public string? HoldCode { get; set; }
        public int NewDraftOrderId { get; set; }
        public string Message { get; set; } = null!;
    }
}
