using System.ComponentModel.DataAnnotations;
namespace Digital_Pharma.Models;
public class PatientCondition {
 public int Id {get;set;} public int UserId {get;set;} public User User {get;set;}=null!;
 [MaxLength(80)] public string Code {get;set;}="";
}
public class PatientAllergy {
 public int Id {get;set;} public int UserId {get;set;} public User User {get;set;}=null!;
 [MaxLength(200)] public string Name {get;set;}="";
 [MaxLength(200)] public string? SubstanceKey {get;set;}
 [MaxLength(500)] public string Reaction {get;set;}="";
 [MaxLength(30)] public string ReportedSeverity {get;set;}="unknown";
 [Timestamp] public byte[] RowVersion {get;set;}=[];
}
public class HealthProfileInput {
 [Required, StringLength(100,MinimumLength=2)] public string UserName {get;set;}="";
 [Required, DataType(DataType.Date)] public DateTime DateOfBirth {get;set;}
 [Required] public string Gender {get;set;}="unknown";
 public bool? IsPregnant {get;set;}
 public List<string> ConditionCodes {get;set;}=[];
 [StringLength(2000)] public string? OtherNotes {get;set;}
 [Required] public string Version {get;set;}="";
}
public class AllergyInput {
 public int Id {get;set;}
 [Required, StringLength(200)] public string Name {get;set;}="";
 public string? SubstanceKey {get;set;}
 [Required, StringLength(500)] public string Reaction {get;set;}="";
 public string ReportedSeverity {get;set;}="unknown";
 public string? Version {get;set;}
}
public record HealthFinding(string Kind,string Priority,string Related,string Message,string? SourceUrl=null,string? SourceDate=null);
public class HealthReview {
 public DateTimeOffset EvaluatedAt {get;set;}=DateTimeOffset.UtcNow;
 public List<HealthFinding> Findings {get;set;}=[];
 public int MedicationCount {get;set;}
 public int LinkedCount {get;set;}
 public string RuleVersion {get;set;}="2026-11-16.2";
}
public class HealthPage {
 public HealthProfileInput Profile {get;set;}=new();
 public List<PatientAllergy> Allergies {get;set;}=[];
 public List<UserMedication> Medications {get;set;}=[];
 public HealthReview Review {get;set;}=new();
 public AllergyInput Allergy {get;set;}=new();
 public Dictionary<string,string> SubstanceOptions {get;set;}=[];
}

