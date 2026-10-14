namespace RE.Application.Models;

public sealed record UsageSummary(
    string Provider,
    string GroupName,
    string BucketName,
    double RemainingPercent,
    DateTimeOffset? ResetTime = null);