namespace Digital_Pharma.Models
{
    public class MultiDrugCheckViewModel
    {
        public List<string> DrugNames { get; set; } = new();
        public List<DrugPairResult> Results { get; set; } = new();
        public string OverallSeverity { get; set; } = "unknown";

        public int DangerCount  => Results.Count(r => r.Severity is "serious" or "critical");
        public int SafeCount    => Results.Count(r => r.Severity is "safe");
        public int UnknownCount => Results.Count(r => r.Severity is "unknown");
        public int TotalPairs   => Results.Count;
    }

    public class DrugPairResult
    {
        public string Drug1 { get; set; } = "";
        public string Drug2 { get; set; } = "";
        public string Severity { get; set; } = "unknown";
        public string Summary { get; set; } = "";
        public string Details { get; set; } = "";
        public string Recommendation { get; set; } = "";
        public string SafeAlternatives { get; set; } = "";
        public bool InteractionFound { get; set; }
        public string EvaluationStatus { get; set; } = "unavailable";
        public string EvidenceNote { get; set; } = "";
        public DateTimeOffset EvaluatedAt { get; set; }
    }
}
