using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GaoApp.Application.DTOs.POS
{
    public sealed class PosActionResultDto
    {
        public string Message { get; set; } = null!;
        public int? OrderId { get; set; }
    }
}
