using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Digital_Pharma.Models
{
    public class Search
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Drug1Name { get; set; } = string.Empty;

        [Required]
        public string Drug2Name { get; set; } = string.Empty;

        public bool InteractionFound { get; set; }

        public string? ResultMessage { get; set; }

        // AI analysis fields
        public string? AiAnalysis { get; set; }
        public string? Severity { get; set; }       // "safe" | "caution" | "moderate" | "serious" | "critical"
        public string? Recommendation { get; set; }
        public string? PatientContext { get; set; } // snapshot of patient profile at time of search

        public string? SafeAlternatives { get; set; }
        public string? ShareToken { get; set; }

        public DateTime SearchDate { get; set; } = DateTime.Now;

        public int? UserId { get; set; }
        [ForeignKey("UserId")]
        public User? User { get; set; }

        public int? DoctorId { get; set; }
        [ForeignKey("DoctorId")]
        public Doctor? Doctor { get; set; }
    }
}
