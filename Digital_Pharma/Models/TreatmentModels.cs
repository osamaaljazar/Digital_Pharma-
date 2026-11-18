using System.ComponentModel.DataAnnotations;
namespace Digital_Pharma.Models;
public class TreatmentInput : MedicationCardInput {
 public int LinkId {get;set;}
 [Required] public string LinkVersion {get;set;}="";
 public string? LabelId {get;set;}
 public Guid OperationId {get;set;}=Guid.NewGuid();
 public string PatientName {get;set;}="";
}
public class TreatmentSearch {
 public int LinkId {get;set;}
 public string Query {get;set;}="";
 public LabelLookup Lookup {get;set;}=new([]);
}
public record TreatmentSnapshot(string DrugName,decimal? DoseAmount,string? DoseUnit,string ScheduleKind,string? DailyTimes,string? Instructions,bool IsStopped) {
 public static TreatmentSnapshot From(UserMedication m)=>new(m.DrugName,m.DoseAmount,m.DoseUnit,m.ScheduleKind,m.DailyTimes,m.Instructions,m.IsStopped);
}
public class TreatmentChange {
 public int Id {get;set;}
 public int PatientId {get;set;} public int DoctorUserId {get;set;} public int LinkId {get;set;} public int MedicationId {get;set;}
 public Guid OperationId {get;set;}
 public string DoctorName {get;set;}="";
 [MaxLength(500)] public string Reason {get;set;}="";
 public string? BeforeJson {get;set;} public string AfterJson {get;set;}="";
 public DateTimeOffset CreatedAt {get;set;}
}
public class PatientNotification {
 public int Id {get;set;} public int PatientId {get;set;}
 public int TreatmentChangeId {get;set;} public TreatmentChange Change {get;set;}=null!;
 public DateTimeOffset? ReadAt {get;set;}
}
public class TreatmentChangePage {
 public TreatmentChange Change {get;set;}=null!;
 public TreatmentSnapshot? Before {get;set;}
 public TreatmentSnapshot After {get;set;}=null!;
}
