using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Data;

/// <summary>
/// DbContext tối giản chỉ dùng để ghi AuditLogs.
/// KHÔNG dùng interceptor.
/// KHÔNG phụ thuộc ITenantContext / ICurrentUser.
/// Mục tiêu:
/// - tránh vòng DI với AppDbContext chính
/// - tránh recursion khi audit log tự ghi chính nó
/// </summary>
public class AuditLogDbContext : DbContext
{
    public AuditLogDbContext(DbContextOptions<AuditLogDbContext> options)
        : base(options)
    {
    }

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Map tối thiểu tới đúng bảng
        modelBuilder.Entity<AuditLog>().ToTable("AuditLogs");
    }
}