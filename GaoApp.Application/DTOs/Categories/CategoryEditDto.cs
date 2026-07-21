using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Categories;

public class CategoryEditDto
{
    public int Id { get; set; }

    [StringLength(30)]
    public string? Code { get; set; } // Create: cho phép rỗng (tự sinh). Edit: readonly ở UI

    [Required(ErrorMessage ="Vui lòng nhập tên loại sản phẩm"), StringLength(200)]
    public string Name { get; set; } = default!;

    public int? ParentId { get; set; }

    public int SortOrder { get; set; } = 0;

    public bool IsActive { get; set; } = true;
}
