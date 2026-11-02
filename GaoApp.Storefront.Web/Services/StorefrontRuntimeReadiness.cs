using GaoApp.Application.Common;
using GaoApp.Application.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Services.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Storefront.Web.Services;

public sealed record StorefrontReadinessCheck(string Code, bool Passed);
public sealed record StorefrontRuntimeAssessment(string Status, string Mode, bool RuntimeReady,
    bool AcceptingNewOrders, IReadOnlyList<StorefrontReadinessCheck> Checks);

public sealed class StorefrontRuntimeReadiness(IConfiguration configuration, IWebHostEnvironment environment, IServiceProvider services)
{
    public async Task<StorefrontRuntimeAssessment> AssessAsync(HttpContext context, CancellationToken ct = default)
    {
        var policy = StorefrontHostPolicy.From(configuration, environment);
        var checks = new List<StorefrontReadinessCheck>();
        StorefrontRuntimeAssessment Block(string code) { checks.Add(new(code, false)); return new("blocked", policy.ModeText, false, false, checks); }
        if (policy.RequestBlocker(context) is { } blocker) return Block(blocker);
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("DefaultConnection"))) return Block("DATABASE_CONFIGURATION_MISSING");
        checks.Add(new("REQUEST_SCOPE_VALID", true));
        try
        {
            var db = services.GetRequiredService<AppDbContext>();
            var store = await db.Stores.AsNoTracking().SingleOrDefaultAsync(x => x.IsActive && x.SubDomainNormalized == policy.Subdomain, ct);
            if (store is null) return Block("STORE_SCOPE_INVALID");
            services.GetRequiredService<ITenantContextWriter>().SetStore(store.Id, store.SubDomainNormalized);
            // Touch actual current tables, not a cached/client supplied ready flag.
            var settings = await db.StorefrontSettings.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == store.Id, ct);
            checks.Add(new("SCHEMA_STORE_READABLE", true));
            try { _ = await services.GetRequiredService<IStorefrontAddressCatalog>().GetAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { return Block("ADDRESS_CATALOG_UNAVAILABLE"); }
            checks.Add(new("ADDRESS_CATALOG_READY", true));
            if (!services.GetRequiredService<StorefrontCheckoutReadiness>().Ready) return Block("KEY_DIRECTORY_INVALID");
            if (await services.GetRequiredService<StorefrontProtectedKeys>().EnsureAsync(db, ct) is { } keyCode) return Block(keyCode);
            checks.Add(new("DURABLE_PROTECTION_READY", true));
            var blockers = await new StorefrontActivationPrerequisites(db, configuration).EvaluateIntakeAsync(ct);
            foreach (var code in blockers) checks.Add(new(code, false));
            checks.Add(new("STORE_INTAKE_ENABLED", settings?.IsEnabled == true));
            // Runtime/maintenance readiness is distinct from permission to take
            // new orders, and neither asserts merchant UAT or live bank testing.
            return new("ready", policy.ModeText, true, settings?.IsEnabled == true && blockers.Count == 0, checks);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return Block("SCHEMA_OR_DATABASE_UNAVAILABLE"); }
    }
}
