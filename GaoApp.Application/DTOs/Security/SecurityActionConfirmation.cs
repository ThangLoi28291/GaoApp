using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Security;

public class SecurityActionConfirmation
{
    [StringLength(128)]
    [DataType(DataType.Password)]
    public string? AdministratorPassword { get; set; }

    [StringLength(500)]
    public string? Reason { get; set; }
}
