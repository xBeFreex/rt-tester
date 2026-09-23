namespace rt_tester.Models;

public sealed class CheckRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string FeedFetchStatusJson { get; set; } = "[]";
    public int FindingCount { get; set; }
}
