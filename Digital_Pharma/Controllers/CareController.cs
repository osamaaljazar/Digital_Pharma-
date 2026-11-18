using System.Security.Claims;
using DB.DBB;
using Digital_Pharma.Models;
using Digital_Pharma.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace DigitalPharma.MVC.Controllers;
[Authorize,AutoValidateAntiforgeryToken]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public class CareController(PharmaDbContext db,HealthPageService health):Controller {
 private int UserId=>int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
 private IQueryable<Doctor> Approved=>db.Doctors.Where(d=>d.Status==DoctorApplicationStatus.Approved && d.IsVerified && !d.User!.IsAdmin && (d.User.LockoutEnd==null || d.User.LockoutEnd<=DateTimeOffset.UtcNow));
 private void Event(CareLink link,string status){link.Status=status;link.UpdatedAt=DateTimeOffset.UtcNow;db.CareLinkEvents.Add(new(){Link=link,ActorUserId=UserId,Status=status,CreatedAt=link.UpdatedAt});}
 [HttpGet,Authorize(Roles="Patient")] public async Task<IActionResult> Index(string? q) {
  var doctors=Approved.AsNoTracking();q=q?.Trim();
  if(!string.IsNullOrEmpty(q))doctors=doctors.Where(d=>d.FullName.Contains(q)||d.Specialization.Contains(q));
  return View(new CareLinksPage {Query=q??"",Doctors=await doctors.OrderBy(d=>d.FullName).Take(30).ToListAsync(),Links=await db.CareLinks.AsNoTracking().Include(l=>l.Doctor).Where(l=>l.PatientId==UserId).OrderByDescending(l=>l.UpdatedAt).ToListAsync()});
 }
 [HttpPost,Authorize(Roles="Patient")] public async Task<IActionResult> SendRequest(int doctorId,bool consent,string? version) {
  if(!consent)return BadRequest("الموافقة الصريحة مطلوبة لمشاركة الملف.");
  if(!await Approved.AnyAsync(d=>d.Id==doctorId && d.UserId!=UserId))return NotFound();
  var link=await db.CareLinks.SingleOrDefaultAsync(l=>l.PatientId==UserId && l.DoctorId==doctorId);
  if(link!=null){if(link.Status is "pending" or "active")return Conflict("يوجد طلب أو ربط قائم بالفعل.");if(version!=Convert.ToBase64String(link.RowVersion))return Conflict("تغير الطلب. أعد تحميل الصفحة.");}
  else {link=new(){PatientId=UserId,DoctorId=doctorId};db.CareLinks.Add(link);}
  link.ConsentedAt=DateTimeOffset.UtcNow;link.CanManageTreatment=false;Event(link,"pending");
  try{await db.SaveChangesAsync();}catch(DbUpdateException){return Conflict("تغير الربط أثناء الحفظ. أعد تحميل الصفحة.");}
  TempData["Success"]="أرسل طلبك للطبيب. لن يُتاح ملفك قبل قبول الطلب.";return RedirectToAction(nameof(Index));
 }
 [HttpGet,Authorize(Roles="Doctor")] public async Task<IActionResult> Patients() {
  var id=await Approved.Where(d=>d.UserId==UserId).Select(d=>d.Id).SingleOrDefaultAsync();if(id==0)return Forbid();
  return View(await db.CareLinks.AsNoTracking().Include(l=>l.Patient).Where(l=>l.DoctorId==id && (l.Status=="active"||l.Status=="pending") && !l.Patient.IsAdmin && !l.Patient.IsDoctorApplicant).OrderByDescending(l=>l.UpdatedAt).ToListAsync());
 }
 [HttpPost,Authorize(Roles="Doctor")] public async Task<IActionResult> Decide(int id,string version,string decision) {
  if(decision is not ("active" or "rejected"))return BadRequest();
  var link=await db.CareLinks.Include(l=>l.Patient).SingleOrDefaultAsync(l=>l.Id==id && l.Doctor.UserId==UserId);
  if(link==null)return NotFound();
  if(!await Approved.AnyAsync(d=>d.Id==link.DoctorId) || link.Patient.IsAdmin || link.Patient.IsDoctorApplicant)return Forbid();
  if(link.Status!="pending" || version!=Convert.ToBase64String(link.RowVersion))return Conflict("الطلب لم يعد بانتظار القرار. حدّث الصفحة.");
  Event(link,decision);try{await db.SaveChangesAsync();}catch(DbUpdateConcurrencyException){return Conflict("تغير الطلب أثناء الحفظ.");}
  return RedirectToAction(nameof(Patients));
 }
 [HttpPost,Authorize(Roles="Patient,Doctor")] public async Task<IActionResult> Revoke(int id,string version) {
  var link=await db.CareLinks.SingleOrDefaultAsync(l=>l.Id==id && ((User.IsInRole("Patient") && l.PatientId==UserId)||(User.IsInRole("Doctor") && l.Doctor.UserId==UserId)));
  if(link==null)return NotFound();
  if(version!=Convert.ToBase64String(link.RowVersion) || link.Status is not ("pending" or "active"))return Conflict("تغيرت حالة الربط. حدّث الصفحة.");
  link.CanManageTreatment=false;Event(link,"revoked");try{await db.SaveChangesAsync();}catch(DbUpdateConcurrencyException){return Conflict("تغير الربط أثناء الحفظ.");}
  TempData["Success"]="ألغي الربط. الطلبات التالية لن تصل إلى الملف.";return RedirectToAction(User.IsInRole("Patient")?nameof(Index):nameof(Patients));
 }
 [HttpPost,Authorize(Roles="Patient")] public async Task<IActionResult> TreatmentPermission(int id,string version,bool allow,bool consent) {
  var link=await db.CareLinks.SingleOrDefaultAsync(l=>l.Id==id && l.PatientId==UserId);
  if(link==null)return NotFound();
  if(link.Status!="active" || version!=Convert.ToBase64String(link.RowVersion))return Conflict("تغير الربط؛ حدّث الصفحة.");
  if(allow && !consent)return BadRequest("تفعيل تعديل العلاج يتطلب موافقة صريحة.");
  if(allow && !await Approved.AnyAsync(d=>d.Id==link.DoctorId))return BadRequest("الطبيب غير معتمد حالياً.");
  link.CanManageTreatment=allow;link.UpdatedAt=DateTimeOffset.UtcNow;
  db.CareLinkEvents.Add(new(){Link=link,ActorUserId=UserId,Status=link.Status,CreatedAt=link.UpdatedAt,Scope=allow?"treatment-write-granted-v1":"treatment-write-withdrawn-v1"});
  try{await db.SaveChangesAsync();}catch(DbUpdateConcurrencyException){return Conflict("تغيرت الموافقة أثناء الحفظ.");}
  TempData["Success"]=allow?"فُعّلت صلاحية تعديل العلاج لهذا الطبيب.":"سُحبت صلاحية تعديل العلاج؛ الاطلاع يبقى حسب الربط.";
  return RedirectToAction(nameof(Index));
 }
 private IQueryable<CareLink> Accessible(int id)=>db.CareLinks.Where(l=>l.Id==id && l.Status=="active" && l.Doctor.UserId==UserId && l.Doctor.Status==DoctorApplicationStatus.Approved && l.Doctor.IsVerified && !l.Patient.IsAdmin && !l.Patient.IsDoctorApplicant);
 [HttpGet,Authorize(Roles="Doctor")] public async Task<IActionResult> Patient(int id,int page=1) {
  var link=await Accessible(id).AsNoTracking().SingleOrDefaultAsync();if(link==null)return NotFound();
  page=Math.Clamp(page,1,100000);
  var model=new CarePatientPage {LinkId=id,CanManageTreatment=link.CanManageTreatment,Page=page,Health=await health.LoadAsync(link.PatientId),Doses=await db.DoseRecords.AsNoTracking().Where(d=>d.UserId==link.PatientId).OrderByDescending(d=>d.Day).ThenByDescending(d=>d.Time).ThenByDescending(d=>d.Id).Skip((page-1)*50).Take(51).ToListAsync(),AlertDecisions=await db.ClinicalAlertDecisions.AsNoTracking().Where(d=>d.CareLinkId==id).OrderByDescending(d=>d.CreatedAt).Take(100).ToListAsync()};
  if(!await Accessible(id).AnyAsync())return NotFound();
  return View(model);
 }

 [HttpPost,Authorize(Roles="Doctor")]
 public async Task<IActionResult> RecordAlertDecision(int linkId,string alertKey,string status,string reason,Guid operationId) {
  if(status is not ("acknowledged" or "monitor" or "dismissed"))return BadRequest("قرار التنبيه غير صالح.");
  reason=reason?.Trim()??"";
  if(reason.Length is < 3 or > 500)return BadRequest("اكتب سبباً واضحاً بين 3 و500 حرف.");
  if(operationId==Guid.Empty)return BadRequest("معرف العملية غير صالح.");
  var link=await Accessible(linkId).AsNoTracking().SingleOrDefaultAsync();if(link==null)return NotFound();
  var review=(await health.LoadAsync(link.PatientId)).Review;
  var finding=review.Findings.SingleOrDefault(f=>ClinicalAlertDecision.Key(f)==alertKey);
  if(finding==null)return Conflict("تغير التنبيه أو لم يعد موجوداً. حدّث صفحة المريض.");
  if(await db.ClinicalAlertDecisions.AnyAsync(d=>d.OperationId==operationId))return Conflict("سُجل هذا القرار سابقاً.");
  db.ClinicalAlertDecisions.Add(new(){CareLinkId=link.Id,PatientId=link.PatientId,DoctorUserId=UserId,OperationId=operationId,AlertKey=alertKey,Status=status,Reason=reason,AlertSnapshot=$"{finding.Kind} | {finding.Priority} | {finding.Related} | {finding.Message}",CreatedAt=DateTimeOffset.UtcNow});
  try{await db.SaveChangesAsync();}catch(DbUpdateException){return Conflict("تعذر حفظ القرار أو تم إرساله سابقاً.");}
  TempData["Success"]="تم توثيق قرارك على التنبيه.";return RedirectToAction(nameof(Patient),new{id=linkId});
 }
}
