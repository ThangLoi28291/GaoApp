using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Options;
using GaoApp.Application.DTOs.Reports.Profit;
using GaoApp.Application.Services.Inventory;
using GaoApp.Application.Services.Reports;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Reports;
using GaoApp.Tests.Inventory;
using GaoApp.Web.Middlewares;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace GaoApp.Tests.Reports;

/// <summary>Isolated loopback service benchmark, not a production/browser/ACB benchmark.</summary>
[Collection("R1FinalDatabasePreflight")]
public sealed class LocalReportLoadTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "LocalLoad")]
    public async Task Mixed_report_order_reads_and_idempotent_stock_writes_remain_consistent()
    {
        var full = Environment.GetEnvironmentVariable("GAOAPP_RUN_LOCAL_LOAD") == "1";
        var orderCount = full ? 2000 : 20;
        var iterations = full ? 20 : 4;
        if (full && int.TryParse(Environment.GetEnvironmentVariable("GAOAPP_LOAD_ITERATIONS"), out var requestedIterations))
            iterations = Math.Clamp(requestedIterations, 20, 1000);
        var levels = full ? new[] { 10, 20, 50 } : new[] { 2 };
        await using var fixture = await SaleCostReversalIntegrationTests.CostFixture.CreateAsync("actual", 10);
        var at = await SeedAsync(fixture, orderCount);
        var periodPolicy = new SalesReportingPeriodPolicy();
        var today = periodPolicy.ConvertUtcToLocal(at).Date;
        var limits = new ProfitReportLimits();
        var profiler = new QueryProfiler();
        using var gate = new ProfitReportExecutionGate(limits);
        var token = Guid.NewGuid().ToString("N");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.GetTempPath() });
        builder.Configuration["AllowedHosts"] = "*";
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        app.UseMiddleware<GlobalExceptionMiddleware>();
        app.Use(async (context, next) =>
        {
            if (context.Request.Headers["X-Local-Load-Token"] != token) { context.Response.StatusCode = 403; return; }
            await next();
        });
        app.MapGet("/load/profit", async (HttpContext context) =>
        {
            await using var db = fixture.Database.CreateTenantContext(fixture.Seed.StoreId, profiler);
            var service = new ProfitReportReadService(new ProfitReportReadRepository(db, limits),
                new CurrentStore(fixture.Seed.StoreId), periodPolicy, new ProfitReportAggregationPolicy(periodPolicy), gate);
            return await service.ReadAsync(new ProfitReportQueryDto {
                FromDate = today.AddDays(-179), ToDate = today, Compare = "previous" }, ct: context.RequestAborted);
        });
        app.MapGet("/load/orders", async (HttpContext context) =>
        {
            await using var db = fixture.Database.CreateTenantContext(fixture.Seed.StoreId);
            return await db.Orders.AsNoTracking().OrderByDescending(x => x.CompletedAtUtc).ThenByDescending(x => x.Id)
                .Select(x => new { x.Id, x.OrderNumber, x.GrandTotal }).Take(25).ToListAsync(context.RequestAborted);
        });
        app.MapPost("/load/stock", async (LoadWrite request, HttpContext context) =>
        {
            if (request.Identity.Length > 50 || !request.Identity.StartsWith("LOAD-", StringComparison.Ordinal))
                return Results.BadRequest();
            await using var db = fixture.Database.CreateTenantContext(fixture.Seed.StoreId);
            var movement = InventoryPosPostingContractTests.CreateRealMovementService(db);
            var command = new InventoryMovementFactory().CreatePurchaseReceipt(fixture.Seed.LegacyWarehouseId,
                fixture.Seed.LegacyVariantId, 1, 10, request.Identity, 1, request.Identity, 1, at);
            await movement.CreateAsync(command, context.RequestAborted);
            // Retry the same canonical identity; it must not increase stock twice.
            await movement.CreateAsync(command, context.RequestAborted);
            return Results.NoContent();
        });
        await app.StartAsync();
        var results = new List<object>();
        double warmupMilliseconds = 0;
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var warmup = Client();
            var warmupClock = Stopwatch.StartNew();
            using (var response = await warmup.GetAsync("/load/profit")) response.EnsureSuccessStatusCode();
            warmupMilliseconds = warmupClock.Elapsed.TotalMilliseconds;
            profiler.Enabled = false;
            WriteArtifact();
            if (Environment.GetEnvironmentVariable("GAOAPP_LOAD_PROFILE_ONLY") == "1") return;
            foreach (var clients in levels)
            {
                var samples = new ConcurrentBag<Sample>();
                var successfulWrites = 0;
                decimal before;
                await using (var db = fixture.Database.CreateTenantContext(fixture.Seed.StoreId))
                    before = (await db.InventoryBalances.SingleAsync(x => x.WarehouseId == fixture.Seed.LegacyWarehouseId &&
                        x.ProductVariantId == fixture.Seed.LegacyVariantId)).OnHandQty;
                using var process = Process.GetCurrentProcess();
                var cpuBefore = process.TotalProcessorTime;
                var clock = Stopwatch.StartNew();
                long peakWorkingSet = 0, peakManaged = 0;
                using var stopSampling = new CancellationTokenSource();
                var sampling = SampleMemory();
                try
                {
                await Task.WhenAll(Enumerable.Range(0, clients).Select(async clientId =>
                {
                    using var client = Client();
                    for (var i = 0; i < iterations; i++)
                    {
                        var kind = (i + clientId) % 10;
                        var endpoint = kind == 0 ? "profit" : kind is 1 or 2 ? "stock" : "orders";
                        var started = Stopwatch.GetTimestamp();
                        try
                        {
                            using var response = endpoint == "stock"
                                ? await client.PostAsJsonAsync("/load/stock", new LoadWrite($"LOAD-{clients}-{clientId}-{i}"))
                                : await client.GetAsync("/load/" + endpoint);
                            samples.Add(new(endpoint, (int)response.StatusCode, Stopwatch.GetElapsedTime(started).TotalMilliseconds));
                            if (response.IsSuccessStatusCode && endpoint == "stock") Interlocked.Increment(ref successfulWrites);
                            if (response.IsSuccessStatusCode && endpoint == "profit")
                            {
                                var report = await response.Content.ReadFromJsonAsync<ProfitReportResponseDto>();
                                Assert.Equal(200 + 20 * orderCount, report!.Current.NetSales.Value);
                                Assert.Equal(100 + 10 * orderCount, report.Current.Cogs.Value);
                            }
                        }
                        catch (HttpRequestException) { samples.Add(new(endpoint, 0, Stopwatch.GetElapsedTime(started).TotalMilliseconds)); }
                        catch (TaskCanceledException) { samples.Add(new(endpoint, 0, Stopwatch.GetElapsedTime(started).TotalMilliseconds)); }
                        await Task.Delay(40 + (i + clientId) % 20);
                    }
                }));
                }
                finally { stopSampling.Cancel(); await sampling; }
                clock.Stop(); process.Refresh();
                decimal after;
                await using (var db = fixture.Database.CreateTenantContext(fixture.Seed.StoreId))
                {
                    after = (await db.InventoryBalances.SingleAsync(x => x.WarehouseId == fixture.Seed.LegacyWarehouseId &&
                        x.ProductVariantId == fixture.Seed.LegacyVariantId)).OnHandQty;
                    Assert.Equal(successfulWrites, await db.InventoryTransactions.CountAsync(x => x.ReferenceId != null && x.ReferenceId.StartsWith($"LOAD-{clients}-")));
                }
                Assert.Equal(before + successfulWrites, after);
                var summaries = samples.GroupBy(x => x.Endpoint).Select(group => new {
                    endpoint = group.Key, requests = group.Count(), success = group.Count(x => x.Status is >= 200 and < 300),
                    rejected = group.Count(x => x.Status == 503), errors = group.Count(x => x.Status < 200 || x.Status >= 400 && x.Status != 503),
                    p50Ms = Percentile(group, .50), p95Ms = Percentile(group, .95), p99Ms = Percentile(group, .99),
                    successP95Ms = Percentile(group.Where(x => x.Status is >= 200 and < 300), .95),
                    statuses = group.GroupBy(x => x.Status).ToDictionary(x => x.Key.ToString(), x => x.Count()) }).ToList();
                results.Add(new {
                    clients, iterationsPerClient = iterations, elapsedSeconds = clock.Elapsed.TotalSeconds,
                    requestsPerSecond = samples.Count / clock.Elapsed.TotalSeconds, endpoints = summaries,
                    combinedClientAndServerCpuPercent = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds /
                        (clock.Elapsed.TotalMilliseconds * Environment.ProcessorCount) * 100,
                    peakWorkingSetMb = peakWorkingSet / 1048576d, peakManagedHeapMb = peakManaged / 1048576d,
                    successfulWrites, stockDelta = after - before, stockAndIdempotencyVerified = true });
                output.WriteLine(JsonSerializer.Serialize(results.Last()));
                WriteArtifact();
                Assert.DoesNotContain(samples, x => x.Status < 200 || x.Status >= 400 && !(x.Endpoint == "profit" && x.Status == 503));

                async Task SampleMemory()
                {
                    try
                    {
                        while (!stopSampling.IsCancellationRequested)
                        {
                            process.Refresh(); peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
                            peakManaged = Math.Max(peakManaged, GC.GetTotalMemory(false));
                            await Task.Delay(100, stopSampling.Token);
                        }
                    }
                    catch (OperationCanceledException) when (stopSampling.IsCancellationRequested) { return; }
                }
            }

            HttpClient Client()
            {
                var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(45) };
                client.DefaultRequestHeaders.Add("X-Local-Load-Token", token); return client;
            }
        }
        finally { await app.StopAsync(); }

        void WriteArtifact()
        {
            if (!full) return;
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "GaoApp.sln"))) root = root.Parent;
            if (root is null) throw new InvalidOperationException("Workspace root was not found.");
            var directory = Path.Combine(root.FullName, "TestResults/security-phase3"); Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "local-load-results.json"), JsonSerializer.Serialize(new {
                generatedAtUtc = DateTime.UtcNow, scope = "Isolated loopback service harness with SQL LocalDB; excludes real login, permission queries, browser, SignalR, external callbacks and host network.",
                operatingSystem = Environment.OSVersion.ToString(), runtime = Environment.Version.ToString(),
                logicalProcessors = Environment.ProcessorCount, availableMemoryMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1048576d,
                syntheticOrders = orderCount + 1, limits, warmupMilliseconds,
                warmupQueryProfile = profiler.Samples, results }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static double Percentile(IEnumerable<Sample> values, double percentile)
    {
        var sorted = values.Select(x => x.Milliseconds).Order().ToArray();
        if (sorted.Length == 0) return 0;
        return Math.Round(sorted[Math.Max(0, (int)Math.Ceiling(sorted.Length * percentile) - 1)], 2);
    }
    public sealed record LoadWrite(string Identity);
    private sealed record Sample(string Endpoint, int Status, double Milliseconds);
    private sealed class CurrentStore(int storeId) : ICurrentStore { public int StoreId => storeId; }

    private sealed class QueryProfiler : DbCommandInterceptor
    {
        public bool Enabled { get; set; } = true;
        public List<object> Samples { get; } = [];
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            // Only synthetic warmup SQL is recorded; parameter values are never recorded.
            if (Enabled) Samples.Add(new { milliseconds = eventData.Duration.TotalMilliseconds, sql = command.CommandText });
            return new(result);
        }
    }

    private static async Task<DateTime> SeedAsync(SaleCostReversalIntegrationTests.CostFixture fixture, int count)
    {
        await using var db = fixture.Database.CreateTenantContext(fixture.Seed.StoreId);
        var original = await db.Orders.SingleAsync(x => x.Id == fixture.Seed.OrderId);
        var at = original.CompletedAtUtc!.Value;
        var orders = Enumerable.Range(0, count).Select(i => new Order { OrderNumber = "LOAD-SEED-" + i,
            POSShiftId = original.POSShiftId, Status = OrderStatus.Completed,
            CompletedAtUtc = at.AddDays(-(i % 180)), Subtotal = 20, GrandTotal = 20 }).ToList();
        db.Orders.AddRange(orders); await db.SaveChangesAsync();
        var lines = orders.Select(x => new OrderLine { OrderId = x.Id, VariantId = fixture.Seed.LegacyVariantId,
            ItemName = "Synthetic rice", Quantity = 1, BaseQuantity = 1, LineTotal = 20 }).ToList();
        db.OrderLines.AddRange(lines); await db.SaveChangesAsync();
        var transactions = lines.Select(x => new InventoryTransaction {
            WarehouseId = fixture.Seed.LegacyWarehouseId, ProductVariantId = x.VariantId,
            TransactionType = InventoryTransactionType.SaleIssue, ReferenceType = InventoryReferenceType.Order,
            ReferenceId = x.OrderId.ToString(), ReferenceLineId = x.Id, QuantityChange = -1, OccurredAtUtc = at }).ToList();
        db.InventoryTransactions.AddRange(transactions); await db.SaveChangesAsync();
        var entries = transactions.Select(tx => new InventoryValuationEntry {
            InventoryTransactionId = tx.Id, WarehouseId = tx.WarehouseId, ProductVariantId = tx.ProductVariantId,
            ReferenceType = tx.ReferenceType, ReferenceId = tx.ReferenceId!, ReferenceLineId = tx.ReferenceLineId,
            EntryType = InventoryValuationEntryType.Outbound, Quantity = -1, UnitCost = 10, Amount = -10, OccurredAtUtc = at,
            CostLayerAllocations = new List<InventoryCostLayerAllocation> { new() { Quantity = 1, UnitCost = 10, Amount = 10,
                IsResolved = true, ResolvedQuantity = 1, ResolvedAmount = 10, ResolvedAtUtc = at } } }).ToList();
        db.InventoryValuationEntries.AddRange(entries); await db.SaveChangesAsync();
        return at;
    }
}
