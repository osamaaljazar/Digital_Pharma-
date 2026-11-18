using System.Security.Claims;
using System.Text.Json;
using DB.DBB;
using Digital_Pharma.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace DigitalPharma.MVC.Controllers;
[Authorize(Roles="Admin"), AutoValidateAntiforgeryToken]
[ResponseCache(NoStore=true, Location=ResponseCacheLocation.None)]
public class AdminController(PharmaDbContext db) : Controller
{
    private int UserId=>int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet]
    public async Task<IActionResult> Index(DoctorApplicationStatus? status, int page=1)
    {
        page=Math.Max(1,page); ViewBag.Page=page; ViewBag.Status=status;
        var query=db.Doctors.AsNoTracking().Include(d=>d.User).AsQueryable();
        if(status.HasValue) query=query.Where(d=>d.Status==status.Value);
        ViewBag.HasNext=await query.CountAsync()>page*25;
        return View(await query.OrderByDescending(d=>d.SubmittedAt).ThenByDescending(d=>d.Id).Skip((page-1)*25).Take(25).ToListAsync());
    }
    [HttpGet]
    public async Task<IActionResult> Review(int id)
    {
        var doctor=await db.Doctors.AsNoTracking().Include(d=>d.User).Include(d=>d.Reviews).ThenInclude(r=>r.ActorUser).SingleOrDefaultAsync(d=>d.Id==id);
        return doctor==null ? NotFound() : View(doctor);
    }
    [HttpPost]
    public async Task<IActionResult> Review(DoctorReviewInput input)
    {
        if(!ModelState.IsValid || !Enum.IsDefined(input.Decision) || input.Decision==DoctorApplicationStatus.Pending)
            return BadRequest("يلزم قرار صحيح وسبب واضح.");
        var doctor=await db.Doctors.SingleOrDefaultAsync(d=>d.Id==input.Id);
        if(doctor==null) return NotFound();
        if(doctor.UserId==int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)) return Forbid();
        if(input.Version!=Convert.ToBase64String(doctor.RowVersion)) return Conflict("سبق تحديث الطلب. أعد تحميل الصفحة.");
        var allowed=doctor.Status switch {
            DoctorApplicationStatus.Pending => input.Decision is DoctorApplicationStatus.Approved or DoctorApplicationStatus.Rejected or DoctorApplicationStatus.NeedsInformation,
            DoctorApplicationStatus.Approved => input.Decision==DoctorApplicationStatus.Suspended,
            DoctorApplicationStatus.Suspended => input.Decision is DoctorApplicationStatus.Approved or DoctorApplicationStatus.NeedsInformation or DoctorApplicationStatus.Rejected,
            _ => false
        };
        if(!allowed) return Conflict("هذا القرار غير متاح لحالة الطلب الحالية.");
        if(input.Decision==DoctorApplicationStatus.Approved && (!input.CredentialsChecked ||
           string.IsNullOrWhiteSpace(doctor.Country) || string.IsNullOrWhiteSpace(doctor.LicensingAuthority) || string.IsNullOrWhiteSpace(doctor.ContactPhone)))
            return BadRequest("استكمل البيانات وأكد التحقق من هوية الطبيب وترخيصه قبل الاعتماد.");
        doctor.Status=input.Decision; doctor.IsVerified=input.Decision==DoctorApplicationStatus.Approved;
        db.DoctorReviews.Add(new DoctorReview { DoctorId=doctor.Id,
            ActorUserId=int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!),
            Status=input.Decision, Note=input.Note.Trim(),
            ApplicationSnapshot=JsonSerializer.Serialize(new { doctor.FullName, doctor.LicenseNumber, doctor.Specialization, doctor.Country, doctor.LicensingAuthority, doctor.ContactPhone })
        });
        try { await db.SaveChangesAsync(); }
        catch(DbUpdateConcurrencyException) { return Conflict("سبق تحديث الطلب. أعد تحميل الصفحة."); }
        TempData["Success"]="تم حفظ القرار وتحديث حالة طلب الطبيب.";
        return RedirectToAction(nameof(Review),new {id=doctor.Id});
    }

    [HttpGet]
    public async Task<IActionResult> Accounts(string? q,string state="all",int page=1)
    {
        page=Math.Clamp(page,1,100000);q=q?.Trim()??"";
        if(state is not ("all" or "active" or "locked"))state="all";
        var now=DateTimeOffset.UtcNow;
        var query=db.Users.AsNoTracking().Where(u=>!u.IsAdmin);
        if(q.Length>0)query=query.Where(u=>u.Email.Contains(q)||u.UserName.Contains(q));
        if(state=="active")query=query.Where(u=>u.LockoutEnd==null||u.LockoutEnd<=now);
        if(state=="locked")query=query.Where(u=>u.LockoutEnd>now);
        var users=await query.OrderBy(u=>u.UserName).ThenBy(u=>u.Id).Skip((page-1)*25).Take(26).ToListAsync();
        var shown=users.Take(25).ToList();var ids=shown.Select(u=>u.Id).ToList();
        return View(new AdminAccountsPage {Users=shown,Query=q,State=state,Page=page,HasNext=users.Count>25,DoctorStatuses=await db.Doctors.AsNoTracking().Where(d=>ids.Contains(d.UserId)).ToDictionaryAsync(d=>d.UserId,d=>d.Status)});
    }

    [HttpPost]
    public async Task<IActionResult> AccountState(int id,string action,string reason)
    {
        reason=reason?.Trim()??"";
        if(action is not ("lock" or "unlock" or "revoke-sessions"))return BadRequest("الإجراء غير صالح.");
        if(reason.Length is < 3 or > 500)return BadRequest("اكتب سبباً بين 3 و500 حرف.");
        if(id==UserId)return BadRequest("لا يمكنك تعديل حالة حساب المدير الحالي.");
        var account=await db.Users.SingleOrDefaultAsync(u=>u.Id==id && !u.IsAdmin);if(account==null)return NotFound();
        if(action=="lock")account.LockoutEnd=DateTimeOffset.UtcNow.AddYears(100);
        else if(action=="unlock")account.LockoutEnd=null;
        account.SessionVersion++;
        db.AdminAccountEvents.Add(new(){TargetUserId=account.Id,ActorUserId=UserId,Action=action,Reason=reason,CreatedAt=DateTimeOffset.UtcNow});
        await db.SaveChangesAsync();TempData["Success"]="تم تحديث حالة الحساب وتسجيل العملية.";
        return RedirectToAction(nameof(Accounts));
    }
}

