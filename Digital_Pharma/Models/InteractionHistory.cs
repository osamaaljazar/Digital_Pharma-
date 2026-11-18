namespace Digital_Pharma.Models;

public sealed class InteractionHistoryPage
{
    public List<InteractionHistoryGroup> Groups { get; set; } = [];
    public int Page { get; set; } = 1;
    public bool HasNext { get; set; }
}

public sealed class InteractionHistoryGroup
{
    public string Key { get; set; } = "";
    public string Kind { get; set; } = "pair";
    public string Severity { get; set; } = "unknown";
    public string EvidenceStatus { get; set; } = "legacy";
    public DateTimeOffset EvaluatedAt { get; set; }
    public List<Search> Results { get; set; } = [];
    public int DrugCount => Results.SelectMany(r => new[] { r.Drug1Name, r.Drug2Name }).Distinct(StringComparer.OrdinalIgnoreCase).Count();
}
