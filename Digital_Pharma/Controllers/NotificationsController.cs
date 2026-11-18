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
public class NotificationsController(PharmaDbContext db,PatientAlertService alerts):Controller {
 private int UserId=>int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
 [HttpGet] public async Task<IActionResult> Index(int page=1){page=Math.Clamp(page,1,100000);return View(new NotificationCenterPage {Page=page,CurrentAlerts=await alerts.CurrentAsync(UserId),TreatmentNotifications=await db.PatientNotifications.AsNoTracking().Include(n=>n.Change).Where(n=>n.PatientId==UserId).OrderByDescending(n=>n.Id).Skip((page-1)*30).Take(31).ToListAsync()});}
 [HttpGet] public async Task<IActionResult> Details(int id){var n=await db.PatientNotifications.AsNoTracking().Include(n=>n.Change).SingleOrDefaultAsync(n=>n.Id==id && n.PatientId==UserId);if(n==null)return NotFound();ViewBag.NotificationId=n.Id;ViewBag.IsRead=n.ReadAt!=null;return View(new TreatmentChangePage {Change=n.Change,Before=n.Change.BeforeJson==null?null:JsonSerializer.Deserialize<TreatmentSnapshot>(n.Change.BeforeJson),After=JsonSerializer.Deserialize<TreatmentSnapshot>(n.Change.AfterJson)!});}
 [HttpPost] public async Task<IActionResult> Read(int id){var n=await db.PatientNotifications.SingleOrDefaultAsync(n=>n.Id==id && n.PatientId==UserId);if(n==null)return NotFound();n.ReadAt??=DateTimeOffset.UtcNow;await db.SaveChangesAsync();return RedirectToAction(nameof(Details),new{id});}
}
