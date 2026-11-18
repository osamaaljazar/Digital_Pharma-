using System.ComponentModel.DataAnnotations;
using System.Globalization;
namespace Digital_Pharma.Models;
public class MedicationCardInput : IValidatableObject {
 public int Id {get;set;}
 public string DrugName {get;set;}="";
 [Required] public string Version {get;set;}="";
 [Range(typeof(decimal),"0.001","999999.999")] public decimal? DoseAmount {get;set;}
 public string? DoseUnit {get;set;}
 [Required] public string ScheduleKind {get;set;}="unspecified";
 [StringLength(200)] public string? DailyTimes {get;set;}
 [Range(typeof(decimal),"0","999999.999")] public decimal? Quantity {get;set;}
 public string? QuantityUnit {get;set;}
 [DataType(DataType.Date)] public DateTime? ExpiryDate {get;set;}
 [StringLength(1000)] public string? Instructions {get;set;}
 [StringLength(1000)] public string? Notes {get;set;}
 [Required] public string Status {get;set;}="active";
 [Required,StringLength(500)] public string ChangeReason {get;set;}="";
 public List<MedicationChange> History {get;set;}=[];
 public static IReadOnlyDictionary<string,string> Units {get;}=new Dictionary<string,string> {
  ["tablet"]="قرص",["capsule"]="كبسولة",["ml"]="مل",["mg"]="ملغ",["g"]="غرام",["drop"]="قطرة",["puff"]="بخة",["unit"]="وحدة",["sachet"]="كيس",["patch"]="لصقة"};
 public static string UnitName(string? key)=>key!=null && Units.TryGetValue(key,out var value)?value:"غير محددة";
 public static string[] Times(string? text)=>(text??"").Split([',','،',';','\n'],StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
 public IEnumerable<ValidationResult> Validate(ValidationContext context) {
  if(Status is not ("active" or "stopped"))yield return new("اختر حالة دواء صالحة.",[nameof(Status)]);
  if(ScheduleKind is not ("unspecified" or "daily" or "as_needed"))yield return new("اختر نوع مواعيد صالحاً.",[nameof(ScheduleKind)]);
  if(DoseAmount.HasValue != !string.IsNullOrWhiteSpace(DoseUnit) || (DoseUnit!=null && !Units.ContainsKey(DoseUnit)))yield return new("أدخل مقدار الجرعة ووحدتها معاً.",[nameof(DoseUnit)]);
  if(Quantity.HasValue != !string.IsNullOrWhiteSpace(QuantityUnit) || (QuantityUnit!=null && !Units.ContainsKey(QuantityUnit)))yield return new("أدخل الكمية ووحدتها معاً.",[nameof(QuantityUnit)]);
  if((DoseAmount.HasValue && decimal.Round(DoseAmount.Value,3)!=DoseAmount) || (Quantity.HasValue && decimal.Round(Quantity.Value,3)!=Quantity))yield return new("استخدم ثلاث خانات عشرية كحد أقصى.");
  var times=Times(DailyTimes);
  if(ScheduleKind=="daily") {
   if(!DoseAmount.HasValue)yield return new("أدخل الجرعة الموصوفة قبل تسجيل مواعيد يومية.",[nameof(DoseAmount)]);
   if(times.Length is <1 or >12 || times.Any(t=>!TimeOnly.TryParseExact(t,"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out _)))yield return new("أدخل من موعد إلى 12 موعداً بصيغة 08:00, 20:00.",[nameof(DailyTimes)]);
   if(times.Distinct().Count()!=times.Length)yield return new("لا تكرر الموعد نفسه.",[nameof(DailyTimes)]);
  } else if(times.Length>0)yield return new("المواعيد الثابتة مخصصة للاستخدام اليومي؛ امسحها عند اختيار نوع آخر.",[nameof(DailyTimes)]);
  if(ScheduleKind=="as_needed" && string.IsNullOrWhiteSpace(Instructions))yield return new("دوّن تعليمات الوصفة للاستخدام عند الحاجة.",[nameof(Instructions)]);
  if(ExpiryDate.HasValue && (ExpiryDate.Value.Year<1900 || ExpiryDate.Value.Year>2200))yield return new("تاريخ الصلاحية غير صالح.",[nameof(ExpiryDate)]);
 }
}
public class MedicationChange {
 public int Id {get;set;} public int UserMedicationId {get;set;}
 public UserMedication Medication {get;set;}=null!;
 public DateTimeOffset CreatedAt {get;set;}
 [MaxLength(500)] public string Reason {get;set;}="";
 public string BeforeJson {get;set;}="";
 public string AfterJson {get;set;}="";
}
