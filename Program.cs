using Microsoft.EntityFrameworkCore;
using rt_tester.Configuration;
using rt_tester.Data;
using rt_tester.Detection;
using rt_tester.Feeds;
using rt_tester.Models;
using rt_tester.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("feeds.json", optional: false, reloadOnChange: true);
builder.Services.Configure<AggregatorConfig>(builder.Configuration.GetSection("Aggregator"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AggregatorConfig>>().Value);

var dbPath = Environment.GetEnvironmentVariable("RT_TESTER_DB_PATH") ?? "rt-tester.db";
builder.Services.AddDbContextFactory<DiagnosticsDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddHttpClient();
builder.Services.AddSingleton<FeedClient>();
builder.Services.AddSingleton<PrefixResolver>();
builder.Services.AddSingleton<EntityHistoryTracker>();
builder.Services.AddSingleton<VehiclePositionComparer>();
builder.Services.AddSingleton<TripUpdateComparer>();
builder.Services.AddSingleton<AlertComparer>();
builder.Services.AddSingleton<MergeDiagnosticsEngine>();
builder.Services.AddSingleton<FindingStore>();
builder.Services.AddHostedService<CheckRunnerHostedService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<DiagnosticsDbContext>>();
    using var db = dbFactory.CreateDbContext();
    db.Database.EnsureCreated();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/findings", async (
    IDbContextFactory<DiagnosticsDbContext> dbFactory,
    string? severity, string? category, string? agency, string? feedType, int? sinceHours) =>
{
    await using var db = await dbFactory.CreateDbContextAsync();
    var query = db.Findings.AsQueryable();

    if (!string.IsNullOrEmpty(severity) && Enum.TryParse<Severity>(severity, true, out var sev))
        query = query.Where(f => f.Severity == sev);
    if (!string.IsNullOrEmpty(category) && Enum.TryParse<MismatchCategory>(category, true, out var cat))
        query = query.Where(f => f.Category == cat);
    if (!string.IsNullOrEmpty(agency))
        query = query.Where(f => f.AgencyId == agency);
    if (!string.IsNullOrEmpty(feedType) && Enum.TryParse<FeedType>(feedType, true, out var ft))
        query = query.Where(f => f.FeedType == ft);
    if (sinceHours is > 0)
    {
        var cutoff = DateTime.UtcNow.AddHours(-sinceHours.Value);
        query = query.Where(f => f.LastSeenUtc >= cutoff);
    }

    var results = await query.OrderByDescending(f => f.LastSeenUtc).Take(500).ToListAsync();
    return Results.Ok(results);
});

app.MapGet("/api/runs", async (IDbContextFactory<DiagnosticsDbContext> dbFactory) =>
{
    await using var db = await dbFactory.CreateDbContextAsync();
    var runs = await db.CheckRuns.OrderByDescending(r => r.StartedAtUtc).Take(20).ToListAsync();
    return Results.Ok(runs);
});

app.MapGet("/api/health", async (IDbContextFactory<DiagnosticsDbContext> dbFactory) =>
{
    await using var db = await dbFactory.CreateDbContextAsync();
    var lastRun = await db.CheckRuns.OrderByDescending(r => r.StartedAtUtc).FirstOrDefaultAsync();
    return Results.Ok(new { lastRun });
});

app.Run();
