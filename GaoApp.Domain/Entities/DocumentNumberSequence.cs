using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Bảng giữ số sequence cuối cùng đã cấp cho từng loại chứng từ theo từng ngày.
/// 
/// Mục tiêu:
/// - cấp số chứng từ an toàn hơn khi nhiều user thao tác cùng lúc
/// - không phụ thuộc quét max số từ bảng business
/// - dễ mở rộng cho nhiều loại phiếu
/// </summary>
[Table("DocumentNumberSequences")]
public class DocumentNumberSequence : BaseStoreEntity
{
    /// <summary>
    /// Loại chứng từ cần cấp số.
    /// </summary>
    public DocumentNumberSequenceType SequenceType { get; set; }

    /// <summary>
    /// Ngày áp dụng sequence.
    /// 
    /// Ví dụ:
    /// 2026-03-26 => NK-20260326-0001, 0002...
    /// </summary>
    public DateTime SequenceDate { get; set; }

    /// <summary>
    /// Số cuối cùng đã cấp.
    /// </summary>
    public int LastNumber { get; set; }
}