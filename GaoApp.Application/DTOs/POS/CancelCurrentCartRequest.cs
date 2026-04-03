using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GaoApp.Application.DTOs.POS
{
    public sealed class CancelCurrentCartRequest
    {
        public string? Reason { get; set; }
    }
}
