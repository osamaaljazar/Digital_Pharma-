using System.ComponentModel.DataAnnotations;

namespace Digital_Pharma.Models;

public class VisitReportOptions
{
    public bool IncludeProfile { get; set; } = true;
    public bool IncludeConditions { get; set; } = true;
    public bool IncludeAllergies { get; set; } = true;
    public bool IncludeMedications { get; set; } = true;
    public bool IncludeDoseSummary { get; set; } = true;
    public bool IncludeRecentChecks { get; set; } = true;
    [StringLength(500)] public string? VisitNote { get; set; }
}

public class VisitReportPage
{
    public DateTimeOffset GeneratedAt { get; set; }
    public User Patient { get; set; } = null!;
    public VisitReportOptions Options { get; set; } = new();
    public List<string> Conditions { get; set; } = [];
    public List<PatientAllergy> Allergies { get; set; } = [];
    public List<UserMedication> Medications { get; set; } = [];
    public int TakenLast30Days { get; set; }
    public int SkippedLast30Days { get; set; }
    public List<Search> RecentChecks { get; set; } = [];
    public string? DoctorName { get; set; }
}

public class ShareManagementPage
{
    public int SearchId { get; set; }
    public string Drug1Name { get; set; } = "";
    public string Drug2Name { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? NewShareUrl { get; set; }
}
