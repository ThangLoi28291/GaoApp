using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GaoApp.Application.DTOs.Media
{
    public sealed class TempUploadRequest
    {
        public required Stream Content { get; init; }
        public required string FileName { get; init; }
        public string? ContentType { get; init; }
        public long SizeBytes { get; init; }
    }
}
