namespace Digital_Pharma.Models
{
    public class DrugInfoResult
    {
        public string Name                { get; set; } = string.Empty;
        public string Category            { get; set; } = string.Empty;
        public string Purpose             { get; set; } = string.Empty;
        public string Mechanism           { get; set; } = string.Empty;
        public string SideEffects         { get; set; } = string.Empty;
        public string Contraindications   { get; set; } = string.Empty;
        public string PregnancySafety     { get; set; } = string.Empty;
        public string KeyInteractions     { get; set; } = string.Empty;
        public string SavedMedConflicts   { get; set; } = string.Empty;
        public string ConditionWarnings   { get; set; } = string.Empty;
        public string DosageNotes         { get; set; } = string.Empty;

        public bool Success               { get; set; } = true;
        public string? ErrorMessage       { get; set; }
        public string QueryName           { get; set; } = string.Empty;
    }
}
