using System.ComponentModel.DataAnnotations;
namespace Digital_Pharma.Models;
public class CareLink {
 public int Id {get;set;} public int PatientId {get;set;} public User Patient {get;set;}=null!;
 public int DoctorId {get;set;} public Doctor Doctor {get;set;}=null!;
 [MaxLength(20)] public string Status {get;set;}="pending";
 public bool CanManageTreatment {get;set;}
 public DateTimeOffset ConsentedAt {get;set;}
 public DateTimeOffset UpdatedAt {get;set;}
 [Timestamp] public byte[] RowVersion {get;set;}=[];
}
public class CareLinkEvent {
 public int Id {get;set;} public int CareLinkId {get;set;} public CareLink Link {get;set;}=null!;
 public int ActorUserId {get;set;}
 [MaxLength(20)] public string Status {get;set;}="";
 public DateTimeOffset CreatedAt {get;set;}
 [MaxLength(200)] public string Scope {get;set;}="health-medications-doses-read-v1";
}
public class CareLinksPage {
 public List<CareLink> Links {get;set;}=[];
 public List<Doctor> Doctors {get;set;}=[];
 public string Query {get;set;}="";
}
public class CarePatientPage {
 public int LinkId {get;set;}
 public bool CanManageTreatment {get;set;}
 public HealthPage Health {get;set;}=new();
 public List<DoseRecord> Doses {get;set;}=[];
 public int Page {get;set;}=1;
 public List<ClinicalAlertDecision> AlertDecisions {get;set;}=[];
}
public static class CareStatus {
 public static string Label(string state)=>state switch {"pending"=>"بانتظار قبول الطبيب","active"=>"ربط فعّال","rejected"=>"رفض الطبيب الطلب",_=>"ربط ملغى"};
}
