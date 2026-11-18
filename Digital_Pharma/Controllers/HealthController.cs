using System.Security.Claims;
using DB.DBB;
using Digital_Pharma.Models;
using Digital_Pharma.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace DigitalPharma.MVC.Controllers;
[Authorize(Roles="Patient"),AutoValidateAntiforgeryToken]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class HealthController(PharmaDbContext db,HealthPageService pages):Controller {
 private int UserId=>int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
 [HttpGet] public async Task<IActionResult> Index()=>View(await pages.LoadAsync(UserId));
 [HttpGet] public async Task<IActionResult> Review()=>View((await pages.LoadAsync(UserId)).Review);
 [HttpPost] public async Task<IActionResult> Save([Bind(Prefix="Profile")] HealthProfileInput input) {
  input.ConditionCodes=input.ConditionCodes.Distinct().ToList();
  if(input.ConditionCodes.Count>30 || input.ConditionCodes.Any(c=>!HealthCatalog.Conditions.Any(d=>d.Code==c))) ModelState.AddModelError("","اختر حالات من القائمة المعروفة.");
  if(input.DateOfBirth.Date>DateTime.Today || input.DateOfBirth.Date<DateTime.Today.AddYears(-120)) ModelState.AddModelError("","تاريخ الميلاد غير صالح.");
  if(input.Gender is not ("male" or "female" or "unknown"))ModelState.AddModelError("","قيمة الجنس غير صالحة.");
  if(input.Gender!="female" && input.IsPregnant.HasValue)ModelState.AddModelError("","حالة الحمل لا تنطبق على الاختيار الحالي؛ اختر غير محدد / لا ينطبق.");
  if(!ModelState.IsValid){var page=await pages.LoadAsync(UserId);page.Profile=input;return View("Index",page);}
  var user=await db.Users.SingleAsync(u=>u.Id==UserId);
  if(input.Version!=Convert.ToBase64String(user.HealthRowVersion))return Conflict("تغير ملفك في نافذة أخرى. أعد تحميل الصفحة قبل الحفظ.");
  var old=await db.PatientConditions.Where(c=>c.UserId==UserId).ToListAsync();
  db.PatientConditions.RemoveRange(old.Where(c=>!input.ConditionCodes.Contains(c.Code)));
  foreach(var code in input.ConditionCodes.Where(c=>old.All(x=>x.Code!=c)))db.PatientConditions.Add(new(){UserId=UserId,Code=code});
  user.UserName=input.UserName.Trim();user.DateOfBirth=input.DateOfBirth.Date;user.Gender=input.Gender;
  user.IsPregnant=input.IsPregnant;user.HealthNotes=input.OtherNotes?.Trim();user.ProfileCompleted=true;
  user.HealthUpdatedAt=DateTimeOffset.UtcNow;
  // Keep the existing AI context in sync, without treating free text as a structured diagnosis.
  user.MedicalConditions=string.Join("، ",HealthCatalog.Conditions.Where(c=>input.ConditionCodes.Contains(c.Code)).Select(c=>c.Name)
   .Concat(string.IsNullOrWhiteSpace(user.HealthNotes)?[]:new[]{user.HealthNotes}));
  try {await db.SaveChangesAsync();}catch(DbUpdateConcurrencyException){return Conflict("تغير الملف أثناء الحفظ. أعد تحميل الصفحة.");}
  TempData["Success"]="تم تحديث الملف وتحديث مراجعة أدويتك.";
  return RedirectToAction(nameof(Index));
 }
 [HttpGet] public async Task<IActionResult> EditAllergy(int id) {
  var page=await pages.LoadAsync(UserId);var item=page.Allergies.SingleOrDefault(a=>a.Id==id);if(item==null)return NotFound();
  page.Allergy=new AllergyInput {Id=item.Id,Name=item.Name,SubstanceKey=item.SubstanceKey,Reaction=item.Reaction,ReportedSeverity=item.ReportedSeverity,Version=Convert.ToBase64String(item.RowVersion)};
  return View("Index",page);
 }
 [HttpPost] public async Task<IActionResult> SaveAllergy([Bind(Prefix="Allergy")] AllergyInput input) {
  var page=await pages.LoadAsync(UserId);
  if(input.ReportedSeverity is not ("unknown" or "mild" or "severe"))ModelState.AddModelError("","شدة التفاعل غير صالحة.");
  if(!string.IsNullOrEmpty(input.SubstanceKey) && !page.SubstanceOptions.ContainsKey(input.SubstanceKey))ModelState.AddModelError("","اختر مادة من الأدوية المرتبطة؛ لا نؤكد مطابقة مادة مكتوبة يدوياً.");
  if(!ModelState.IsValid){page.Allergy=input;return View("Index",page);}
  PatientAllergy item;
  if(input.Id==0){item=new(){UserId=UserId};db.PatientAllergies.Add(item);}
  else {var existing=await db.PatientAllergies.SingleOrDefaultAsync(a=>a.Id==input.Id && a.UserId==UserId);
   if(existing==null)return NotFound();item=existing;
   if(input.Version!=Convert.ToBase64String(item.RowVersion))return Conflict("تغير سجل الحساسية؛ أعد تحميل الصفحة.");}
  item.SubstanceKey=string.IsNullOrWhiteSpace(input.SubstanceKey)?null:input.SubstanceKey;
  item.Name=item.SubstanceKey==null?input.Name.Trim():page.SubstanceOptions[item.SubstanceKey];
  item.Reaction=input.Reaction.Trim();item.ReportedSeverity=input.ReportedSeverity;
  try {await db.SaveChangesAsync();}catch(DbUpdateConcurrencyException){return Conflict("تغير سجل الحساسية؛ أعد تحميل الصفحة.");}
  TempData["Success"]="حُفظت الحساسية وأعيدت مراجعة المواد الفعالة.";
  return RedirectToAction(nameof(Index));
 }
 [HttpPost] public async Task<IActionResult> DeleteAllergy(int id,string version) {
  var item=await db.PatientAllergies.SingleOrDefaultAsync(a=>a.Id==id && a.UserId==UserId);
  if(item==null)return NotFound();if(version!=Convert.ToBase64String(item.RowVersion))return Conflict("تغير السجل؛ أعد تحميل الصفحة.");
  db.PatientAllergies.Remove(item);
  try {await db.SaveChangesAsync();}catch(DbUpdateConcurrencyException){return Conflict("تغير السجل؛ أعد تحميل الصفحة.");}
  TempData["Success"]="حُذفت الحساسية وأعيد حساب التنبيهات.";
  return RedirectToAction(nameof(Index));
 }
}

