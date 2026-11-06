namespace Digital_Pharma.Models
{
    public class ClaudeAnalysisResult
    {
        public string Severity { get; set; } = "unknown";
        public string Summary { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public string Recommendation { get; set; } = string.Empty;
        public string PatientSpecificNotes { get; set; } = string.Empty;
        public string SafeAlternatives { get; set; } = string.Empty;
        public bool InteractionFound { get; set; }
        public string SavedMedConflicts { get; set; } = string.Empty;
        public bool Success { get; set; } = true;
        public string? ErrorMessage { get; set; }
    }
}
