using System.Data;
using System.Security.Claims;
using System.Text.Json;
using DB.DBB;
using Digital_Pharma.Models;
using Digital_Pharma.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace DigitalPharma.MVC.Controllers;
[Authorize(Roles="Doctor"),AutoValidateAntiforgeryToken]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public class TreatmentController(PharmaDbContext db,IDrugLabelClient labels):Controller {
 private int UserId=>int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
 private IQueryable<CareLink> Allowed(int id)=>db.CareLinks.Where(l=>l.Id==id && l.Status=="active" && l.CanManageTreatment && l.Doctor.UserId==UserId && l.Doctor.Status==DoctorApplicationStatus.Approved && l.Doctor.IsVerified && !l.Doctor.User!.IsAdmin && (l.Doctor.User.LockoutEnd==null || l.Doctor.User.LockoutEnd<=DateTimeOffset.UtcNow) && !l.Patient.IsAdmin && !l.Patient.IsDoctorApplicant && !db.Doctors.Any(d=>d.UserId==l.PatientId));
 [HttpGet] public async Task<IActionResult> Search(int linkId,string? q,CancellationToken ct) {
  if(!await Allowed(linkId).AnyAsync(ct))return NotFound();
  return View(new TreatmentSearch {LinkId=linkId,Query=q??"",Lookup=string.IsNullOrWhiteSpace(q)?new([]):await labels.SearchAsync(q,ct)});
 }
 [HttpGet] public async Task<IActionResult> Edit(int linkId,int id=0,string? labelId=null,CancellationToken ct=default) {
  var link=await Allowed(linkId).AsNoTracking().Include(l=>l.Patient).SingleOrDefaultAsync(ct);if(link==null)return NotFound();
  UserMedication? m=null;DrugLabel? label=null;
  if(id!=0){m=await db.UserMedications.AsNoTracking().SingleOrDefaultAsync(m=>m.Id==id && m.UserId==link.PatientId,ct);if(m==null)return NotFound();}
  else {var lookup=await labels.ByIdAsync(labelId??"",ct);label=lookup.Labels.SingleOrDefault(l=>l.Id.Equals(labelId,StringComparison.OrdinalIgnoreCase));if(label==null){TempData["LabelError"]=lookup.Error??"تعذر جلب المنتج المحدد.";return RedirectToAction(nameof(Search),new{linkId});}}
  return View(new TreatmentInput {LinkId=link.Id,LinkVersion=Convert.ToBase64String(link.RowVersion),PatientName=link.Patient.UserName,
   Id=m?.Id??0,Version=m==null?"new":Convert.ToBase64String(m.RowVersion),LabelId=label?.Id,DrugName=m?.DrugName??label!.DisplayName,
   DoseAmount=m?.DoseAmount,DoseUnit=m?.DoseUnit,ScheduleKind=m?.ScheduleKind??"unspecified",DailyTimes=m?.DailyTimes,Instructions=m?.Instructions,Status=m?.IsStopped==true?"stopped":"active"});
 }
 [HttpPost] public async Task<IActionResult> Edit(TreatmentInput input,CancellationToken ct) {
  if(!await Allowed(input.LinkId).AnyAsync(ct))return NotFound();
  if(input.OperationId==Guid.Empty)ModelState.AddModelError("","معرف العملية مفقود؛ أعد فتح النموذج.");
  if(input.Status=="active" && (!input.DoseAmount.HasValue || string.IsNullOrEmpty(input.DoseUnit)))ModelState.AddModelError("","أدخل الجرعة ووحدتها عند إضافة العلاج أو استئنافه.");
  if(!ModelState.IsValid){
   var currentLink=await Allowed(input.LinkId).AsNoTracking().Include(l=>l.Patient).SingleOrDefaultAsync(ct);if(currentLink==null)return NotFound();
   input.PatientName=currentLink.Patient.UserName;
   if(input.Id!=0){var current=await db.UserMedications.AsNoTracking().SingleOrDefaultAsync(m=>m.Id==input.Id && m.UserId==currentLink.PatientId,ct);if(current==null)return NotFound();input.DrugName=current.DrugName;}
   else input.DrugName="منتج جديد — تحقق من الحقول المطلوبة";
   return View(input);
  }
  DrugLabel? label=null;
  if(input.Id==0){var lookup=await labels.ByIdAsync(input.LabelId??"",ct);label=lookup.Labels.SingleOrDefault(l=>l.Id.Equals(input.LabelId,StringComparison.OrdinalIgnoreCase));if(label==null)return BadRequest("تعذر التحقق من هوية الدواء؛ لم يُحفظ أي تغيير.");}
  return await db.Database.CreateExecutionStrategy().ExecuteAsync<IActionResult>(async()=>{
   db.ChangeTracker.Clear();await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
   var link=await Allowed(input.LinkId).Include(l=>l.Doctor).SingleOrDefaultAsync(ct);if(link==null)return NotFound();
   if(input.LinkVersion!=Convert.ToBase64String(link.RowVersion))return Conflict("تغيرت موافقة المريض. أعد فتح النموذج.");
   if(await db.TreatmentChanges.AnyAsync(c=>c.OperationId==input.OperationId,ct))return Conflict("هذه العملية محفوظة بالفعل. حدّث الصفحة.");
   UserMedication m;string? before=null;
   if(input.Id==0){
    if(await db.UserMedications.AnyAsync(m=>m.UserId==link.PatientId && m.LabelId==label!.Id,ct))return Conflict("الدواء موجود؛ عدّل بطاقته بدلاً من إضافته ثانية.");
    m=new(){UserId=link.PatientId,DrugName=label!.DisplayName,LabelId=label.Id,LabelJson=JsonSerializer.Serialize(label),LabelRetrievedAt=DateTimeOffset.UtcNow};db.UserMedications.Add(m);
   }else{
    var found=await db.UserMedications.SingleOrDefaultAsync(m=>m.Id==input.Id && m.UserId==link.PatientId,ct);if(found==null)return NotFound();m=found;
    if(input.Version!=Convert.ToBase64String(m.RowVersion))return Conflict("تغير الدواء في نافذة أخرى. أعد تحميل البطاقة.");
    before=JsonSerializer.Serialize(TreatmentSnapshot.From(m));
   }
   m.DoseAmount=input.DoseAmount;m.DoseUnit=input.DoseUnit;m.ScheduleKind=input.ScheduleKind;
   m.DailyTimes=input.ScheduleKind=="daily"?string.Join(", ",MedicationCardInput.Times(input.DailyTimes).Order()):null;
   m.Instructions=input.Instructions?.Trim();m.IsStopped=input.Status=="stopped";m.UpdatedAt=DateTimeOffset.UtcNow;
   var after=JsonSerializer.Serialize(TreatmentSnapshot.From(m));
   if(before==after)return BadRequest("لم تتغير بيانات العلاج.");
   try {
    await db.SaveChangesAsync(ct);
    var change=new TreatmentChange {PatientId=link.PatientId,DoctorUserId=UserId,DoctorName=link.Doctor.FullName,LinkId=link.Id,MedicationId=m.Id,OperationId=input.OperationId,Reason=input.ChangeReason.Trim(),BeforeJson=before,AfterJson=after,CreatedAt=DateTimeOffset.UtcNow};
    db.TreatmentChanges.Add(change);db.PatientNotifications.Add(new(){PatientId=link.PatientId,Change=change});
    await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
   }catch(DbUpdateException){return Conflict("تغيرت البيانات أثناء الحفظ؛ حدّث الصفحة للتحقق من النتيجة.");}
   TempData["Success"]="حُفظت الخطة وسجل التغيير، وأضيف إشعار داخل حساب المريض.";
   return RedirectToAction("Patient","Care",new{id=link.Id});
  });
 }
}
