using System.Security.Claims;
using System.Text.Json;
using DB.DBB;
using Digital_Pharma.Models;
using Digital_Pharma.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace DigitalPharma.MVC.Controllers;
[Authorize(Roles="Patient"),AutoValidateAntiforgeryToken]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class MedicationsController(PharmaDbContext db,IDrugLabelClient labels):Controller {
 private int UserId=>int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
 private static object Snapshot(UserMedication m)=>new {m.DoseAmount,m.DoseUnit,m.ScheduleKind,m.DailyTimes,m.Quantity,m.QuantityUnit,m.ExpiryDate,m.Instructions,m.Notes,m.IsStopped};
 [HttpGet] public async Task<IActionResult> Edit(int id) {
  var m=await db.UserMedications.AsNoTracking().SingleOrDefaultAsync(m=>m.Id==id && m.UserId==UserId);
  if(m==null)return NotFound();
  return View(new MedicationCardInput {Id=m.Id,DrugName=m.DrugName,Version=Convert.ToBase64String(m.RowVersion),DoseAmount=m.DoseAmount,DoseUnit=m.DoseUnit,
   ScheduleKind=m.ScheduleKind,DailyTimes=m.DailyTimes,Quantity=m.Quantity,QuantityUnit=m.QuantityUnit,ExpiryDate=m.ExpiryDate,Instructions=m.Instructions,Notes=m.Notes,
   Status=m.IsStopped?"stopped":"active",History=await db.MedicationChanges.AsNoTracking().Where(c=>c.UserMedicationId==id).OrderByDescending(c=>c.Id).Take(20).ToListAsync()});
 }
 [HttpPost] public async Task<IActionResult> Edit(MedicationCardInput input) {
  var m=await db.UserMedications.SingleOrDefaultAsync(m=>m.Id==input.Id && m.UserId==UserId);
  if(m==null)return NotFound();
  input.DrugName=m.DrugName;
  if(!ModelState.IsValid) {
   input.History=await db.MedicationChanges.AsNoTracking().Where(c=>c.UserMedicationId==m.Id).OrderByDescending(c=>c.Id).Take(20).ToListAsync();
   return View(input);
  }
  if(input.Version!=Convert.ToBase64String(m.RowVersion))return Conflict("تغيرت بطاقة الدواء في نافذة أخرى. أعد تحميلها قبل الحفظ.");
  var before=JsonSerializer.Serialize(Snapshot(m));
  m.DoseAmount=input.DoseAmount;m.DoseUnit=input.DoseUnit;m.ScheduleKind=input.ScheduleKind;
  m.DailyTimes=input.ScheduleKind=="daily"?string.Join(", ",MedicationCardInput.Times(input.DailyTimes).Order()):null;
  m.Quantity=input.Quantity;m.QuantityUnit=input.QuantityUnit;m.ExpiryDate=input.ExpiryDate?.Date;
  m.Instructions=input.Instructions?.Trim();m.Notes=input.Notes?.Trim();m.IsStopped=input.Status=="stopped";m.UpdatedAt=DateTimeOffset.UtcNow;
  db.MedicationChanges.Add(new(){Medication=m,CreatedAt=m.UpdatedAt.Value,Reason=input.ChangeReason.Trim(),BeforeJson=before,AfterJson=JsonSerializer.Serialize(Snapshot(m))});
  try{await db.SaveChangesAsync();}catch(DbUpdateConcurrencyException){return Conflict("تغيرت البطاقة أثناء الحفظ. أعد تحميل الصفحة.");}
  TempData["MedSuccess"]="حُفظت بطاقة الدواء وتحدّثت مراجعة أدويتك.";
  return RedirectToAction("MyMedications","Home");
 }
 [HttpPost] public async Task<IActionResult> AddStock(RestockInput input) {
  var medication=await db.UserMedications.SingleOrDefaultAsync(m=>m.Id==input.Id && m.UserId==UserId);
  if(medication==null)return NotFound();
  if(!MedicationCardInput.Units.ContainsKey(input.Unit))ModelState.AddModelError(nameof(input.Unit),"اختر وحدة كمية صالحة.");
  if(medication.Quantity.HasValue && !string.Equals(medication.QuantityUnit,input.Unit,StringComparison.Ordinal))ModelState.AddModelError(nameof(input.Unit),"وحدة العبوة الجديدة يجب أن تطابق وحدة المخزون الحالية.");
  if(!ModelState.IsValid){TempData["MedError"]=string.Join(" ",ModelState.Values.SelectMany(v=>v.Errors).Select(e=>e.ErrorMessage));return RedirectToAction("MyMedications","Home");}
  if(input.Version!=Convert.ToBase64String(medication.RowVersion))return Conflict("تغير المخزون في نافذة أخرى. أعد تحميل الصفحة.");
  var before=JsonSerializer.Serialize(Snapshot(medication));
  medication.Quantity=(medication.Quantity??0)+input.Amount;medication.QuantityUnit=input.Unit;medication.UpdatedAt=DateTimeOffset.UtcNow;
  db.MedicationChanges.Add(new(){Medication=medication,CreatedAt=medication.UpdatedAt.Value,Reason=input.Reason.Trim(),BeforeJson=before,AfterJson=JsonSerializer.Serialize(Snapshot(medication))});
  try{await db.SaveChangesAsync();}catch(DbUpdateConcurrencyException){return Conflict("تغير المخزون أثناء الحفظ. أعد تحميل الصفحة.");}
  TempData["MedSuccess"]=$"أضيفت {input.Amount:0.###} {MedicationCardInput.UnitName(input.Unit)} إلى مخزون {medication.DrugName}.";
  return RedirectToAction("MyMedications","Home");
 }
 [HttpGet] public async Task<IActionResult> Search(string? q,int? medicationId,CancellationToken ct) {
  if(medicationId.HasValue && !await db.UserMedications.AnyAsync(m=>m.Id==medicationId && m.UserId==UserId,ct))return NotFound();
  return View(new LabelSearchPage {Query=q??"",MedicationId=medicationId,
   Lookup=string.IsNullOrWhiteSpace(q)?new([]):await labels.SearchAsync(q,ct)});
 }
 [HttpPost] public async Task<IActionResult> Add(string labelId,int? medicationId,string? notes,CancellationToken ct) {
  if((notes?.Length??0)>1000)return BadRequest("الملاحظات طويلة.");
  UserMedication? medication=null;
  if(medicationId.HasValue){medication=await db.UserMedications.SingleOrDefaultAsync(m=>m.Id==medicationId && m.UserId==UserId,ct);if(medication==null)return NotFound();}
  var lookup=await labels.ByIdAsync(labelId,ct);
  var label=lookup.Labels.SingleOrDefault(l=>l.Id.Equals(labelId,StringComparison.OrdinalIgnoreCase));
  if(label==null){TempData["LabelError"]=lookup.Error??"لم يثبت تطابق المنتج؛ لم يتم حفظ الربط.";return RedirectToAction(nameof(Search),new {medicationId});}
  if(await db.UserMedications.AnyAsync(m=>m.UserId==UserId && m.LabelId==label.Id && (!medicationId.HasValue || m.Id!=medicationId),ct)){
   TempData["LabelError"]="هذه النشرة مرتبطة بدواء في قائمتك بالفعل.";return RedirectToAction(nameof(Search),new {medicationId});}
  if(medication==null){medication=new(){UserId=UserId};db.UserMedications.Add(medication);}
  medication.DrugName=label.DisplayName;medication.LabelId=label.Id;medication.LabelJson=JsonSerializer.Serialize(label);
  medication.LabelRetrievedAt=DateTimeOffset.UtcNow;if(notes!=null)medication.Notes=notes.Trim();
  try{await db.SaveChangesAsync(ct);}catch(DbUpdateException){TempData["LabelError"]="تغيرت قائمة الأدوية أثناء الحفظ. حدّث الصفحة.";return RedirectToAction(nameof(Search));}
  TempData["Success"]="حُفظ المنتج المرتبط بنشرته. راجع التنبيهات أدناه.";
  return RedirectToAction("Review","Health");
 }
}

