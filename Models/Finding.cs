namespace rt_tester.Models;

public sealed class Finding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CheckRunId { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public FeedType FeedType { get; set; }
    public MismatchCategory Category { get; set; }
    public Severity Severity { get; set; }
    public string? AgencyId { get; set; }
    public string EntityKey { get; set; } = "";
    public string? FieldName { get; set; }
    public string? SourceValue { get; set; }
    public string? MergedValue { get; set; }
    public string Explanation { get; set; } = "";
    public string? LikelyValidatorCode { get; set; }
    public string? SourceEntityJson { get; set; }
    public string? MergedEntityJson { get; set; }
    public string Fingerprint { get; set; } = "";
}
