namespace Digital_Pharma.Models
{
    public class PregnancyDrugItem
    {
        public string Name        { get; set; } = string.Empty;
        public string Category    { get; set; } = string.Empty;
        public string Risk        { get; set; } = string.Empty;
        public string Alternative { get; set; } = string.Empty;
        public string Severity    { get; set; } = "serious";
    }
}
