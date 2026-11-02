# WEB01 schema metadata tool

Run from the repository root. This helper builds the EF model without opening a
database connection; its connection string is a metadata-only placeholder.

```powershell
dotnet run --project tools/validation/storefront-web01-schema/Schema.csproj --no-restore
dotnet run --project tools/validation/storefront-web01-schema/Schema.csproj --no-restore -- manifest
```

The first command compares the current model and snapshot. `manifest` also
validates every migration prefix through GaoApp's schema manifest catalog.

`scaffold` is a development helper used to generate WEB01's structural migration,
designer and snapshot from an explicitly validated model diff. It rejects drops,
changes to unrelated tables, and overwriting an existing WEB01 migration. The
checked-in migration additionally includes reviewed permission-only SQL,
immutable-evidence triggers and the data-preserving downgrade guard; those hooks
are maintained in `StorefrontFoundationSchemaSqlCatalog` and must be retained if
the migration is regenerated during development. This tool never applies SQL.

The tool uses EF 10.0.13's internal SQL Server design-time registration solely for
code generation. No new dependency or design-time helper is loaded by either
runtime Web host.
