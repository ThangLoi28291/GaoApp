using GaoApp.Application.DTOs.Audit;

namespace GaoApp.Application.Interfaces.Services.Audit;

/// <summary>
/// Lấy thông tin runtime hiện tại từ request/user/store.
/// </summary>
public interface IAuditExecutionContextAccessor
{
    AuditExecutionContextDto GetCurrent();
}