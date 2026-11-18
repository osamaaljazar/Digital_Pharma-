using System.Security.Claims;
using System.Text.Json;
using DB.DBB;
using Digital_Pharma.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DigitalPharma.MVC.Controllers;

[Authorize, AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class DoctorController(PharmaDbContext db) : Controller
{
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet]
    public async Task<IActionResult> Apply()
    {
        if (User.IsInRole("Admin")) return Forbid();
        var doctor = await db.Doctors.SingleOrDefaultAsync(d => d.UserId == UserId);
        if (doctor != null && doctor.Status is not (DoctorApplicationStatus.NeedsInformation or DoctorApplicationStatus.Rejected))
            return RedirectToAction(nameof(Status));
        return View(doctor == null ? new DoctorApplicationInput() : new DoctorApplicationInput {
            FullName=doctor.FullName, LicenseNumber=doctor.LicenseNumber, Specialization=doctor.Specialization,
            Country=doctor.Country, LicensingAuthority=doctor.LicensingAuthority, ContactPhone=doctor.ContactPhone,
            Version=Convert.ToBase64String(doctor.RowVersion)
        });
    }
    [HttpPost]
    public async Task<IActionResult> Apply(DoctorApplicationInput input)
    {
        if (User.IsInRole("Admin")) return Forbid();
        if (!ModelState.IsValid) return View(input);
        var doctor = await db.Doctors.SingleOrDefaultAsync(d => d.UserId == UserId);
        if (doctor != null)
        {
            if (doctor.Status is not (DoctorApplicationStatus.NeedsInformation or DoctorApplicationStatus.Rejected))
                return Conflict("لا يمكن تعديل الطلب في حالته الحالية.");
            if (input.Version != Convert.ToBase64String(doctor.RowVersion))
                return Conflict("تغير الطلب. أعد تحميل الصفحة.");
        }
        else { doctor = new Doctor { UserId=UserId }; db.Doctors.Add(doctor); }
        doctor.FullName=input.FullName.Trim(); doctor.LicenseNumber=input.LicenseNumber.Trim();
        doctor.Specialization=input.Specialization.Trim(); doctor.Country=input.Country.Trim();
        doctor.LicensingAuthority=input.LicensingAuthority.Trim(); doctor.ContactPhone=input.ContactPhone.Trim();
        doctor.Status=DoctorApplicationStatus.Pending; doctor.IsVerified=false;
        doctor.SubmittedAt=DateTimeOffset.UtcNow;
        var account=await db.Users.SingleAsync(u=>u.Id==UserId);
        account.IsDoctorApplicant=true;
        db.DoctorReviews.Add(new DoctorReview { Doctor=doctor, ActorUserId=UserId,
            Status=doctor.Status, Note="إرسال الطلب للمراجعة",
            ApplicationSnapshot=JsonSerializer.Serialize(new { doctor.FullName, doctor.LicenseNumber, doctor.Specialization, doctor.Country, doctor.LicensingAuthority, doctor.ContactPhone }) });
        try { await db.SaveChangesAsync(); }
        catch(DbUpdateConcurrencyException) { return Conflict("تغير الطلب. أعد تحميل الصفحة."); }
        catch(DbUpdateException) { return Conflict("تعذر حفظ الطلب؛ حدّث الصفحة وتحقق من حالة طلبك."); }
        return RedirectToAction(nameof(Status));
    }
    [HttpGet]
    public async Task<IActionResult> Status()
    {
        var doctor=await db.Doctors.Include(d=>d.Reviews).SingleOrDefaultAsync(d=>d.UserId==UserId);
        if(doctor==null) return RedirectToAction(nameof(Apply));
        return View(doctor);
    }
    [HttpGet, Authorize(Roles="Doctor")]
    public IActionResult Dashboard() => View();
}

