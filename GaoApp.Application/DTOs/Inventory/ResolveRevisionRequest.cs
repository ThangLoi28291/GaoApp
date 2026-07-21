using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Inventory;

/// <summary>
/// Request cho quản lý xử lý yêu cầu sửa phiếu nhập.
/// </summary>
public class ResolveRevisionRequest
{
    /// <summary>
    /// true: trả phiếu về cho nhân viên sửa.
    /// false: bỏ qua yêu cầu sửa, phiếu vẫn chờ duyệt.
    /// </summary>
    public bool ReturnToEdit { get; set; }

    [StringLength(1000)]
    public string? Note { get; set; }
}