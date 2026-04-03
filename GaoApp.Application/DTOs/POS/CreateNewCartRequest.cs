using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GaoApp.Application.DTOs.POS
{
    public sealed class CreateNewCartRequest
    {
        public int? CustomerId { get; set; }
        public string? Note { get; set; }
    }
}
