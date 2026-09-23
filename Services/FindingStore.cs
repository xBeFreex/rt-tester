using Microsoft.EntityFrameworkCore;
using rt_tester.Configuration;
using rt_tester.Data;
using rt_tester.Models;

namespace rt_tester.Services;

public sealed class FindingStore(IDbContextFactory<DiagnosticsDbContext> dbFactory, AggregatorConfig config)
{
    public async Task<Guid> StartRunAsync(DateTime startedAtUtc, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var run = new CheckRun { StartedAtUtc = startedAtUtc };
        db.CheckRuns.Add(run);
        await db.SaveChangesAsync(ct);
        return run.Id;
    }

    public async Task SaveFindingsAndCompleteRunAsync(Guid runId, List<Finding> findings, string feedFetchStatusJson, DateTime completedAtUtc, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        foreach (var finding in findings)
        {
            var existing = await db.Findings
                .Where(f => f.Fingerprint == finding.Fingerprint)
                .OrderByDescending(f => f.LastSeenUtc)
                .FirstOrDefaultAsync(ct);

            if (existing is not null)
            {
                existing.LastSeenUtc = finding.LastSeenUtc;
                existing.CheckRunId = finding.CheckRunId;
                existing.SourceValue = finding.SourceValue;
                existing.MergedValue = finding.MergedValue;
                existing.SourceEntityJson = finding.SourceEntityJson;
                existing.MergedEntityJson = finding.MergedEntityJson;
            }
            else
            {
                db.Findings.Add(finding);
            }
        }

        var run = await db.CheckRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is not null)
        {
            run.CompletedAtUtc = completedAtUtc;
            run.FeedFetchStatusJson = feedFetchStatusJson;
            run.FindingCount = findings.Count;
        }

        await db.SaveChangesAsync(ct);

        var cutoff = completedAtUtc.AddHours(-config.HistoryRetentionHours);
        var stale = await db.Findings.Where(f => f.LastSeenUtc < cutoff).ToListAsync(ct);
        db.Findings.RemoveRange(stale);
        var staleRuns = await db.CheckRuns.Where(r => r.StartedAtUtc < cutoff).ToListAsync(ct);
        db.CheckRuns.RemoveRange(staleRuns);
        await db.SaveChangesAsync(ct);
    }
}
