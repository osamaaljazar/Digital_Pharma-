using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace Digital_Pharma.Models;
public class DoseRecord {
 public int Id {get;set;} public int UserId {get;set;} public int MedicationId {get;set;}
 [Column(TypeName="date")] public DateTime Day {get;set;}
 [MaxLength(5)] public string Time {get;set;}="";
 [MaxLength(20)] public string Status {get;set;}="unrecorded";
 public string DrugName {get;set;}="";
 [Column(TypeName="decimal(12,3)")] public decimal DoseAmount {get;set;}
 [MaxLength(30)] public string DoseUnit {get;set;}="";
 [Column(TypeName="decimal(12,3)")] public decimal StockDeducted {get;set;}
 public DateTimeOffset RecordedAt {get;set;}
 [Timestamp] public byte[] RowVersion {get;set;}=[];
}
public class DoseChange {
 public int Id {get;set;} public int DoseRecordId {get;set;} public DoseRecord Record {get;set;}=null!;
 [MaxLength(20)] public string BeforeStatus {get;set;}="";
 [MaxLength(20)] public string AfterStatus {get;set;}="";
 [MaxLength(500)] public string Reason {get;set;}="";
 public DateTimeOffset ChangedAt {get;set;}
}
public record DoseSlot(UserMedication Medication,string Time,DoseRecord? Record);
public class DosesPage {
 public DateTime Day {get;set;}
 public string NowTime {get;set;}="";
 public List<DoseSlot> Slots {get;set;}=[];
 public List<DoseRecord> OtherRecords {get;set;}=[];
 public int UnscheduledCount {get;set;}
 public int TakenLast7Days {get;set;}
 public int SkippedLast7Days {get;set;}
 public decimal? RecordedAdherencePercent {get;set;}
}
public class DoseCorrection {
 public int Id {get;set;}
 [Required] public string Version {get;set;}="";
 [Required] public string Status {get;set;}="";
 [Required,StringLength(500)] public string Reason {get;set;}="";
 public DoseRecord? Record {get;set;}
 public List<DoseChange> Changes {get;set;}=[];
}
public static class DoseStatus {
 public static string Label(string value)=>value switch {"taken"=>"تم تناولها","skipped"=>"تم تخطيها",_=>"غير مسجلة"};
}
