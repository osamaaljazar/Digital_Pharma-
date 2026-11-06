namespace Digital_Pharma.Models
{
    public class InteractionAnalysis
    {
        public OpenFdaResult Drug1 { get; set; } = new();
        public OpenFdaResult Drug2 { get; set; } = new();
        public string DisplayDrug1 { get; set; } = string.Empty;
        public string DisplayDrug2 { get; set; } = string.Empty;

        public bool InteractionFound { get; set; }
        public string AnalysisMessage { get; set; } = string.Empty;
        public string Severity { get; set; } = "unknown";

        // Claude AI fields
        public string AiDetails { get; set; } = string.Empty;
        public string AiRecommendation { get; set; } = string.Empty;
        public string AiPatientNotes { get; set; } = string.Empty;
        public string AiSafeAlternatives { get; set; } = string.Empty;
        public bool AiSuccess { get; set; } = true;

        // Saved medication conflicts
        public string AiSavedMedConflicts { get; set; } = string.Empty;

        // For sharing
        public int? SearchId { get; set; }
        public string? ShareToken { get; set; }
    }
}
