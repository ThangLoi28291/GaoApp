namespace GaoApp.Infrastructure.Data.Migrations;

public readonly record struct DatabaseObjectIdentity
{
    public DatabaseObjectIdentity(string? schema, string name)
    {
        Schema = DatabaseSchemaNormalization.NormalizeIdentifier(
            string.IsNullOrWhiteSpace(schema) ? "dbo" : schema);
        Name = DatabaseSchemaNormalization.NormalizeIdentifier(name);
    }

    public string Schema { get; }
    public string Name { get; }
}

public sealed record DatabaseObjectInventory(
    IReadOnlySet<DatabaseObjectIdentity> Tables,
    IReadOnlySet<DatabaseObjectIdentity> Views,
    IReadOnlySet<DatabaseObjectIdentity> Procedures,
    IReadOnlySet<DatabaseObjectIdentity> Functions,
    IReadOnlySet<DatabaseObjectIdentity> Sequences,
    IReadOnlySet<DatabaseObjectIdentity> Synonyms,
    IReadOnlySet<string> UserSchemas,
    IReadOnlySet<DatabaseObjectIdentity> UserDefinedTypes,
    IReadOnlySet<DatabaseObjectIdentity> OtherStructuralObjects,
    DatabaseSecurityMetadataInventory SecurityMetadata)
{
    public int TotalStructuralObjectCount =>
        Tables.Count
        + Views.Count
        + Procedures.Count
        + Functions.Count
        + Sequences.Count
        + Synonyms.Count
        + UserSchemas.Count
        + UserDefinedTypes.Count
        + OtherStructuralObjects.Count;

    public int StructuralNonTableObjectCount =>
        Views.Count
        + Procedures.Count
        + Functions.Count
        + Sequences.Count
        + Synonyms.Count
        + UserSchemas.Count
        + UserDefinedTypes.Count
        + OtherStructuralObjects.Count;
}

public sealed record DatabaseSecurityMetadataInventory(
    IReadOnlySet<string> DatabaseUsers,
    IReadOnlySet<string> CustomDatabaseRoles,
    IReadOnlySet<DatabaseRoleMembershipIdentity> RoleMemberships,
    IReadOnlySet<string> Certificates,
    IReadOnlySet<string> AsymmetricKeys,
    IReadOnlySet<string> SymmetricKeys,
    IReadOnlySet<string> DatabaseScopedCredentials)
{
    public DatabaseSecurityMetadataCounts Counts => new(
        DatabaseUsers.Count,
        CustomDatabaseRoles.Count,
        RoleMemberships.Count,
        Certificates.Count,
        AsymmetricKeys.Count,
        SymmetricKeys.Count,
        DatabaseScopedCredentials.Count);
}

public readonly record struct DatabaseRoleMembershipIdentity
{
    public DatabaseRoleMembershipIdentity(string role, string member)
    {
        Role = DatabaseSchemaNormalization.NormalizeIdentifier(role);
        Member = DatabaseSchemaNormalization.NormalizeIdentifier(member);
    }

    public string Role { get; }
    public string Member { get; }
}

public sealed record DatabaseSecurityMetadataCounts(
    int DatabaseUsers,
    int CustomDatabaseRoles,
    int RoleMemberships,
    int Certificates,
    int AsymmetricKeys,
    int SymmetricKeys,
    int DatabaseScopedCredentials)
{
    public static DatabaseSecurityMetadataCounts Empty { get; } = new(
        DatabaseUsers: 0,
        CustomDatabaseRoles: 0,
        RoleMemberships: 0,
        Certificates: 0,
        AsymmetricKeys: 0,
        SymmetricKeys: 0,
        DatabaseScopedCredentials: 0);
}

public interface ISqlServerDatabaseObjectInventoryReader
{
    Task<DatabaseObjectInventory> ReadAsync(
        CancellationToken ct = default);
}
