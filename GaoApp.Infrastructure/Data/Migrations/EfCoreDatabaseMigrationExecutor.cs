using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Data.Migrations;

public sealed class EfCoreDatabaseMigrationExecutor
    : IDatabaseMigrationExecutor
{
    private readonly AppDbContext _db;

    public EfCoreDatabaseMigrationExecutor(AppDbContext db)
    {
        _db = db;
    }

    public Task MigrateAsync(CancellationToken ct = default)
        => _db.Database.MigrateAsync(ct);
}
