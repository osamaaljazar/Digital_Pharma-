namespace Digital_Pharma.Services;
public record ConditionDefinition(string Code,string Name,string[] LabelTerms);
public static class HealthCatalog {
 // Internal identifiers, not diagnostic codes and not a complete medical terminology.
 public static readonly ConditionDefinition[] Conditions=[
 new("diabetes","السكري",["diabetes","diabetic"]),
 new("hypertension","ارتفاع ضغط الدم",["hypertension","high blood pressure"]),
 new("asthma","الربو",["asthma"]),
 new("kidney_disease","مرض كلوي — المرحلة غير محددة",["renal impairment","kidney disease"]),
 new("severe_renal","قصور كلوي شديد موثق: eGFR أقل من 30",["severe renal impairment"]),
 new("liver_disease","مرض كبدي",["hepatic impairment","liver disease"]),
 new("heart_failure","قصور القلب",["heart failure"]),
 new("ulcer","قرحة المعدة أو الاثني عشر",["peptic ulcer","gastric ulcer"]),
 new("bleeding_disorder","اضطراب نزف",["bleeding disorder"]),
 new("epilepsy","الصرع",["epilepsy","seizure"]),
 new("metabolic_acidosis","حماض استقلابي مشخص",["metabolic acidosis"]),
 new("thyroid_disease","مرض بالغدة الدرقية",["thyroid"])
 ];
 public static string Normalize(string value)=>string.Join(" ",value.Trim().ToUpperInvariant().Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries));
}

