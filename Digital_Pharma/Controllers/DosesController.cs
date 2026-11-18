using System.Data;
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
public class DosesController(PharmaDbContext db):Controller {
 private int UserId=>int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
 [HttpGet] public async Task<IActionResult> Index() {
  var now=DoseSchedule.Now;var day=now.Date;
  var meds=await db.UserMedications.AsNoTracking().Where(m=>m.UserId==UserId && !m.IsStopped).ToListAsync();
  var records=await db.DoseRecords.AsNoTracking().Where(d=>d.UserId==UserId && d.Day==day).ToListAsync();
  var since=day.AddDays(-6);
  var recent=await db.DoseRecords.AsNoTracking().Where(d=>d.UserId==UserId && d.Day>=since && d.Day<=day && (d.Status=="taken" || d.Status=="skipped")).ToListAsync();
  var taken=recent.Count(d=>d.Status=="taken");var skipped=recent.Count(d=>d.Status=="skipped");
  var slots=meds.SelectMany(m=>DoseSchedule.Times(m).Select(t=>new DoseSlot(m,t,records.SingleOrDefault(d=>d.MedicationId==m.Id && d.Time==t)))).OrderBy(s=>s.Time).ThenBy(s=>s.Medication.DrugName).ToList();
  return View(new DosesPage {Day=day,NowTime=now.ToString("HH:mm"),Slots=slots,UnscheduledCount=meds.Count(m=>DoseSchedule.Times(m).Length==0),OtherRecords=records.Where(d=>!slots.Any(s=>s.Record?.Id==d.Id)).ToList(),TakenLast7Days=taken,SkippedLast7Days=skipped,RecordedAdherencePercent=taken+skipped==0?null:decimal.Round(taken*100m/(taken+skipped),0)});
 }
 [HttpPost] public async Task<IActionResult> Record(int medicationId,DateTime day,string time,string status,string version) {
  if(!ModelState.IsValid || status is not ("taken" or "skipped"))return BadRequest("بيانات التسجيل غير صالحة.");
  var now=DoseSchedule.Now;
  if(day!=now.Date || string.CompareOrdinal(time,now.ToString("HH:mm"))>0)return BadRequest("يمكن تسجيل موعد اليوم بعد حلوله فقط. أعد تحميل الجدول.");
  return await db.Database.CreateExecutionStrategy().ExecuteAsync<IActionResult>(async () => {
  db.ChangeTracker.Clear();
  await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
  var m=await db.UserMedications.SingleOrDefaultAsync(m=>m.Id==medicationId && m.UserId==UserId);
  if(m==null)return NotFound();
  if(version!=Convert.ToBase64String(m.RowVersion) || !DoseSchedule.Times(m).Contains(time))return Conflict("تغيرت بطاقة الدواء أو الموعد. أعد تحميل الجدول.");
  if(await db.DoseRecords.AnyAsync(d=>d.UserId==UserId && d.MedicationId==medicationId && d.Day==day && d.Time==time))return Conflict("سبق تسجيل هذا الموعد. استخدم تصحيح التسجيل.");
  var deducted=0m;
  if(status=="taken" && MedicationInsights.CanDeduct(m)){
   deducted=Math.Min(m.Quantity!.Value,m.DoseAmount!.Value);m.Quantity-=deducted;m.UpdatedAt=DateTimeOffset.UtcNow;
  }
  var record=new DoseRecord {UserId=UserId,MedicationId=m.Id,Day=day,Time=time,Status=status,DrugName=m.DrugName,DoseAmount=m.DoseAmount!.Value,DoseUnit=m.DoseUnit!,StockDeducted=deducted,RecordedAt=DateTimeOffset.UtcNow};
  db.DoseRecords.Add(record);db.DoseChanges.Add(new(){Record=record,BeforeStatus="unrecorded",AfterStatus=status,Reason="التسجيل الأول",ChangedAt=record.RecordedAt});
  try {await db.SaveChangesAsync();await tx.CommitAsync();}catch(DbUpdateException){return Conflict("تعذر الحفظ بسبب تغيير متزامن. أعد تحميل الجدول قبل المحاولة.");}
  TempData["Success"]="حُفظ تسجيل الجرعة.";return RedirectToAction(nameof(Index));
  });
 }
 [HttpGet] public async Task<IActionResult> History(int page=1) {
  page=Math.Max(1,Math.Min(page,100000));ViewBag.Page=page;
  return View(await db.DoseRecords.AsNoTracking().Where(d=>d.UserId==UserId).OrderByDescending(d=>d.Day).ThenByDescending(d=>d.Time).ThenByDescending(d=>d.Id).Skip((page-1)*50).Take(51).ToListAsync());
 }
 private async Task<DoseCorrection?> Correction(int id) {
  var record=await db.DoseRecords.AsNoTracking().SingleOrDefaultAsync(d=>d.Id==id && d.UserId==UserId);
  if(record==null)return null;
  return new(){Id=id,Record=record,Status=record.Status,Version=Convert.ToBase64String(record.RowVersion),Changes=await db.DoseChanges.AsNoTracking().Where(c=>c.DoseRecordId==id).OrderByDescending(c=>c.Id).ToListAsync()};
 }
 [HttpGet] public async Task<IActionResult> Correct(int id) {var input=await Correction(id);return input==null?NotFound():View(input);}
 [HttpPost] public async Task<IActionResult> Correct(DoseCorrection input) {
  var record=await db.DoseRecords.AsNoTracking().SingleOrDefaultAsync(d=>d.Id==input.Id && d.UserId==UserId);if(record==null)return NotFound();
  if(input.Status is not ("taken" or "skipped" or "unrecorded"))ModelState.AddModelError("Status","اختر حالة صالحة.");
  if(!ModelState.IsValid){var page=await Correction(input.Id);input.Record=page!.Record;input.Changes=page.Changes;return View(input);}
  if(input.Version!=Convert.ToBase64String(record.RowVersion))return Conflict("تغير تسجيل الجرعة. أعد تحميل الصفحة.");
  if(input.Status==record.Status)return BadRequest("لم تتغير حالة التسجيل.");
  return await db.Database.CreateExecutionStrategy().ExecuteAsync<IActionResult>(async()=>{
   db.ChangeTracker.Clear();await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
   var current=await db.DoseRecords.SingleOrDefaultAsync(d=>d.Id==input.Id && d.UserId==UserId);if(current==null)return NotFound();
   if(input.Version!=Convert.ToBase64String(current.RowVersion))return Conflict("تغير تسجيل الجرعة. أعد تحميل الصفحة.");
   var medication=await db.UserMedications.SingleOrDefaultAsync(m=>m.Id==current.MedicationId && m.UserId==UserId);
   if(medication!=null && current.Status=="taken" && current.StockDeducted>0){medication.Quantity=(medication.Quantity??0)+current.StockDeducted;current.StockDeducted=0;medication.UpdatedAt=DateTimeOffset.UtcNow;}
   if(medication!=null && input.Status=="taken" && MedicationInsights.CanDeduct(medication)){var amount=Math.Min(medication.Quantity!.Value,current.DoseAmount);medication.Quantity-=amount;current.StockDeducted=amount;medication.UpdatedAt=DateTimeOffset.UtcNow;}
   db.DoseChanges.Add(new(){Record=current,BeforeStatus=current.Status,AfterStatus=input.Status,Reason=input.Reason.Trim(),ChangedAt=DateTimeOffset.UtcNow});
   current.Status=input.Status;current.RecordedAt=DateTimeOffset.UtcNow;
   try{await db.SaveChangesAsync();await tx.CommitAsync();}catch(DbUpdateException){return Conflict("تغير التسجيل أو المخزون أثناء الحفظ. أعد تحميل الصفحة.");}
   TempData["Success"]="تم تصحيح التسجيل وتحديث المخزون مع حفظ تاريخ التعديل.";return RedirectToAction(nameof(History));
  });
 }
}
