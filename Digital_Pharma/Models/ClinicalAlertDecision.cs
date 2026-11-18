using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;

namespace Digital_Pharma.Models;

public class ClinicalAlertDecision
{
    public int Id { get; set; }
    public int CareLinkId { get; set; }
    public CareLink Link { get; set; } = null!;
    public int PatientId { get; set; }
    public int DoctorUserId { get; set; }
    public Guid OperationId { get; set; }
    [MaxLength(64)] public string AlertKey { get; set; } = "";
    [MaxLength(20)] public string Status { get; set; } = "acknowledged";
    [MaxLength(500)] public string Reason { get; set; } = "";
    [MaxLength(2000)] public string AlertSnapshot { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public static string Key(HealthFinding finding)
    {
        var value = $"{finding.Kind}|{finding.Related}|{finding.SourceUrl}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    public static string StatusLabel(string status) => status switch
    {
        "monitor" => "متابعة ومراقبة",
        "dismissed" => "لا ينطبق حالياً",
        _ => "تمت المراجعة"
    };
}
