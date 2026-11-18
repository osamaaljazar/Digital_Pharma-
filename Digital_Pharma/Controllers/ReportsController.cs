using System.Security.Claims;
using DB.DBB;
using Digital_Pharma.Models;
using Digital_Pharma.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DigitalPharma.MVC.Controllers;

[Authorize(Roles = AccountRoles.Patient)]
[AutoValidateAntiforgeryToken]
public class ReportsController(PharmaDbContext db) : Controller
{
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public IActionResult Index() => View(new VisitReportOptions());

    [HttpPost]
    public async Task<IActionResult> Preview(VisitReportOptions options, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View("Index", options);
        if (!(options.IncludeProfile || options.IncludeConditions || options.IncludeAllergies ||
              options.IncludeMedications || options.IncludeDoseSummary || options.IncludeRecentChecks))
        {
            ModelState.AddModelError("", "اختر قسماً واحداً على الأقل لإدراجه في التقرير.");
            return View("Index", options);
        }

        var patient = await db.Users.AsNoTracking().SingleAsync(u => u.Id == UserId, cancellationToken);
        var page = new VisitReportPage
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Patient = patient,
            Options = options
        };

        if (options.IncludeConditions)
        {
            var codes = await db.PatientConditions.AsNoTracking().Where(c => c.UserId == UserId)
                .Select(c => c.Code).ToListAsync(cancellationToken);
            page.Conditions = codes.Select(code => HealthCatalog.Conditions.FirstOrDefault(c => c.Code == code)?.Name ?? code).ToList();
        }
        if (options.IncludeAllergies)
            page.Allergies = await db.PatientAllergies.AsNoTracking().Where(a => a.UserId == UserId).OrderBy(a => a.Id).ToListAsync(cancellationToken);
        if (options.IncludeMedications)
            page.Medications = await db.UserMedications.AsNoTracking().Where(m => m.UserId == UserId && !m.IsStopped).OrderBy(m => m.DrugName).ToListAsync(cancellationToken);
        if (options.IncludeDoseSummary)
        {
            var since = DateTime.Today.AddDays(-29);
            var doses = await db.DoseRecords.AsNoTracking().Where(d => d.UserId == UserId && d.Day >= since)
                .Select(d => d.Status).ToListAsync(cancellationToken);
            page.TakenLast30Days = doses.Count(s => s == "taken");
            page.SkippedLast30Days = doses.Count(s => s == "skipped");
        }
        if (options.IncludeRecentChecks)
            page.RecentChecks = await db.Searches.AsNoTracking().Where(s => s.UserId == UserId)
                .OrderByDescending(s => s.EvaluatedAt).ThenByDescending(s => s.SearchDate).Take(10).ToListAsync(cancellationToken);

        page.DoctorName = await db.CareLinks.AsNoTracking().Where(l => l.PatientId == UserId && l.Status == "active")
            .Select(l => l.Doctor.FullName).FirstOrDefaultAsync(cancellationToken);
        return View("Preview", page);
    }
}
