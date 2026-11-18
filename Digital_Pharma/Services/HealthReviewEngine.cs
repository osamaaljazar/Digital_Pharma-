using System.Text.Json;
using System.Text.RegularExpressions;
using Digital_Pharma.Models;
namespace Digital_Pharma.Services;
public sealed class HealthReviewEngine {
 public static DrugLabel? ReadLabel(UserMedication med) {
  if(string.IsNullOrWhiteSpace(med.LabelJson))return null;
  try { return JsonSerializer.Deserialize<DrugLabel>(med.LabelJson)?.Normalize(); }catch(JsonException){return null;}
 }
 public static Dictionary<string,string> Substances(IEnumerable<UserMedication> meds)=>meds.Select(ReadLabel)
  .Where(l=>l!=null).SelectMany(l=>l!.Names.Substances)
  .Where(n=>!string.IsNullOrWhiteSpace(n)).DistinctBy(HealthCatalog.Normalize)
  .ToDictionary(HealthCatalog.Normalize,n=>n);
 public HealthReview Evaluate(User user,IEnumerable<PatientCondition> conditions,IEnumerable<PatientAllergy> allergies,IEnumerable<UserMedication> medications) {
  var allMeds=medications.ToList();var meds=allMeds.Where(m=>!m.IsStopped).ToList();var diagnoses=conditions.ToList();var allergyList=allergies.ToList();
  var review=new HealthReview {MedicationCount=meds.Count};
  void Add(string kind,string priority,string related,string message,DrugLabel? label=null)=>
   review.Findings.Add(new(kind,priority,related,message,label?.SourceUrl,label?.EffectiveTime));
  if(allMeds.Any(m=>m.IsStopped)) Add("stopped","incomplete","الأدوية المتوقفة","المراجعة الحالية تشمل الأدوية المسجلة كمستمرة فقط؛ لا تقيّم الآثار المتبقية للأدوية التي أوقفتها.");
  if(!user.ProfileCompleted) Add("missing","incomplete","الملف الصحي","الملف الصحي غير مكتمل؛ أكمل البيانات لتوسيع المراجعة.");
  if(!string.IsNullOrWhiteSpace(user.HealthNotes)) Add("missing","incomplete","ملاحظات غير مرمّزة","الملاحظات الحرة محفوظة لكنها لا تفسر آلياً كتشخيص. اختر الحالات المطابقة من القائمة عند توفرها.");
  foreach(var allergy in allergyList.Where(a=>a.SubstanceKey==null))
   Add("missing","incomplete",allergy.Name,"الحساسية مسجلة دون مادة فعالة مرتبطة؛ لا يمكن فحصها آلياً. يشمل ذلك أسماء العائلات الدوائية والتفاعلات المتصالبة.");
  foreach(var med in meds) {
   var label=ReadLabel(med);
   if(label==null || label.Names.Substances.Length==0) {Add("missing","incomplete",med.DrugName,"لم تربط مادة فعالة موثقة بهذا الدواء؛ اختر نشرته قبل مراجعة الدواء.");continue;}
   review.LinkedCount++;
   if(med.LabelRetrievedAt==null || med.LabelRetrievedAt<DateTimeOffset.UtcNow.AddDays(-30))
    Add("stale","incomplete",med.DrugName,"نسخة النشرة قديمة؛ أعد ربط المنتج لتحديثها.",label);
   var ingredients=label.Names.Substances.Select(HealthCatalog.Normalize).ToHashSet();
   foreach(var allergy in allergyList.Where(a=>a.SubstanceKey!=null && ingredients.Contains(a.SubstanceKey))) {
    var severe=allergy.ReportedSeverity=="severe";
    Add("allergy",severe?"urgent":"review",med.DrugName+" ↔ "+allergy.Name,
     severe
      ?"تطابق اسم المادة الفعالة مع حساسية سجلتها كشديدة. لا تتناول جرعة جديدة قبل التواصل العاجل مع طبيب أو صيدلي، واطلب مساعدة طارئة عند وجود صعوبة تنفس أو تورم شديد. المطابقة لا تفحص الحساسية المتصالبة."
      :"تطابق اسم المادة في النشرة مع الحساسية التي سجلتها. راجع الطبيب أو الصيدلي قبل الاستخدام؛ لا يحدد هذا الفحص شدة التفاعل ولا يفحص الحساسية المتصالبة.",label);
   }
   var contraindications=string.Join(" ",label.Contraindications);
   var warnings=string.Join(" ",label.Warnings.Concat(label.WarningsAndCautions));
   foreach(var condition in diagnoses) {
    var definition=HealthCatalog.Conditions.SingleOrDefault(c=>c.Code==condition.Code);
    if(definition==null) {Add("missing","incomplete",condition.Code,"هذه الحالة غير معرّفة في قاموس المراجعة.");continue;}
    var contraindicationTerm=definition.LabelTerms.FirstOrDefault(t=>ContainsTerm(contraindications,t));
    var warningTerm=definition.LabelTerms.FirstOrDefault(t=>ContainsTerm(warnings,t));
    if(contraindicationTerm!=null) Add("label-context","urgent",med.DrugName+" ↔ "+definition.Name,
      "ورد مصطلح مرتبط بهذه الحالة ("+contraindicationTerm+") داخل قسم موانع الاستعمال في النشرة. لا توقف العلاج بنفسك، لكن راجع الطبيب أو الصيدلي قبل الجرعة التالية للتحقق من انطباق النص على حالتك.",label);
    else if(warningTerm!=null) Add("label-context","review",med.DrugName+" ↔ "+definition.Name,
      "ورد مصطلح مرتبط بهذه الحالة ("+warningTerm+") داخل قسم التحذيرات والاحتياطات في النشرة. هذا ربط نصي للمراجعة وليس تشخيصاً؛ افتح النشرة وراجع المختص لفهم السياق.",label);
    else Add("coverage","incomplete",med.DrugName+" ↔ "+definition.Name,"لم نجد إشارة مطابقة في الأقسام المتاحة. لا توجد نتيجة أمان مؤكدة لهذا الاستخدام.",label);
   }
   // A narrowly scoped, sourced rule. Do not infer kidney severity from generic kidney disease.
   if(ingredients.Contains("METFORMIN HYDROCHLORIDE") || ingredients.Contains("METFORMIN")) {
    const string source="https://dailymed.nlm.nih.gov/dailymed/drugInfo.cfm?setid=73197cb5-3e23-4ac0-b189-f9bc848bce66";
    if(diagnoses.Any(c=>c.Code=="severe_renal"))
     review.Findings.Add(new("condition-rule","review",med.DrugName+" ↔ القصور الكلوي الشديد","قاعدة محددة للميتفورمين: النشرة تذكر مانع استعمال عند eGFR أقل من 30. تنطبق المطابقة على الحالة الشديدة التي سجلتها، وليس كل مرض كلوي. يلزم مراجعة المختص للخطة؛ لا نغيّر العلاج تلقائياً.",source,"2026-09-03"));
    if(diagnoses.Any(c=>c.Code=="metabolic_acidosis"))
     review.Findings.Add(new("condition-rule","review",med.DrugName+" ↔ الحماض الاستقلابي","تذكر نشرة الميتفورمين الحماض الاستقلابي الحاد أو المزمن ضمن موانع الاستعمال. راجع المختص لمراجعة الخطة بناءً على التشخيص المسجل.",source,"2026-09-03"));
   }
   if(user.IsPregnant!=false && user.Gender=="female") {
    var pregnancy=label.Pregnancy.Length+label.PregnancyOrBreastFeeding.Length>0;
    Add("pregnancy",pregnancy?"review":"incomplete",med.DrugName+" ↔ حالة الحمل",
     user.IsPregnant==null?"حالة الحمل غير محددة؛ استكملها. لا نستنتجها من الحالة الاجتماعية.":
     pregnancy?"توجد معلومات عن الحمل في النشرة؛ يلزم تقييمها مع المختص. توفر الفقرة لا يعني أن الدواء آمن أو ممنوع.":"لم تتوفر فقرة حمل ضمن البيانات المسترجعة؛ تعذر تقييم الاستخدام.",label);
   }
  }
  for(int i=0;i<meds.Count;i++)for(int j=i+1;j<meds.Count;j++){
   var a=ReadLabel(meds[i]);var b=ReadLabel(meds[j]);if(a==null||b==null)continue;
   var overlap=a.Names.Substances.Select(HealthCatalog.Normalize).Intersect(b.Names.Substances.Select(HealthCatalog.Normalize)).ToArray();
   if(overlap.Length>0)Add("duplicate","review",meds[i].DrugName+" ↔ "+meds[j].DrugName,
    "المادتان الدوائيتان المسجلتان تشتركان في: "+string.Join("، ",overlap)+". تحقق من عدم تكرار العلاج مع المختص؛ لم تُحسب الجرعة الإجمالية.",a);
   var mentioned=b.Names.Substances.Any(t=>ContainsTerm(string.Join(" ",a.Interactions),t)) ||
                 a.Names.Substances.Any(t=>ContainsTerm(string.Join(" ",b.Interactions),t));
   Add("drug-pair",mentioned?"review":"incomplete",meds[i].DrugName+" ↔ "+meds[j].DrugName,
    mentioned?"ورد اسم مادة من الدواء الآخر في فقرة التداخلات. راجع سياق النشرة؛ هذه مطابقة نصية وليست تصنيفاً سريرياً للتداخل.":
    "لم يكتمل فحص التداخل السريري بين هذين الدواءين. عدم العثور على تطابق نصي لا يعني عدم وجود تداخل.",mentioned && b.Names.Substances.Any(t=>ContainsTerm(string.Join(" ",a.Interactions),t))?a:b);
  }
  if(meds.Count==0)Add("empty","incomplete","قائمة الأدوية","أضف دواءً مرتبطاً بنشرته لمراجعته وفق حالتك الصحية وحساسياتك.");
  return review;
 }
 private static bool ContainsTerm(string text,string term)=>!string.IsNullOrWhiteSpace(term) &&
  Regex.IsMatch(text,@"(?<![\p{L}\p{N}])"+Regex.Escape(term)+@"(?![\p{L}\p{N}])",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
}

