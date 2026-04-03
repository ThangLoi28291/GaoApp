using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GaoApp.Application.DTOs.POS
{
    public sealed class ScanBarcodeToCurrentCartRequest
    {
        public string Barcode { get; set; } = null!;
        public decimal Quantity { get; set; } = 1;
    }
}
