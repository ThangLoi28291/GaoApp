using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Data.Migrations;

public sealed class EfCoreDatabaseMigrationCatalog
    : IDatabaseMigrationCatalog
{
    private readonly AppDbContext _db;

    public EfCoreDatabaseMigrationCatalog(AppDbContext db)
    {
        _db = db;
    }

    public DatabaseMigrationCatalogSnapshot CreateSnapshot()
        => new(_db.Database.GetMigrations().ToList());
}
