using System.Security.Claims;
using DB.DBB;
using Digital_Pharma.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace DigitalPharma.MVC.ViewComponents;
public sealed record NotificationNavModel(int Count,int? LatestId,string? DoctorName);
public class NotificationLinkViewComponent(PharmaDbContext db,PatientAlertService alerts):ViewComponent {
 public async Task<IViewComponentResult> InvokeAsync(){
  var id=int.Parse(HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
  var unread=db.PatientNotifications.AsNoTracking().Where(n=>n.PatientId==id && n.ReadAt==null);
  var count=await unread.CountAsync()+(await alerts.CurrentAsync(id)).Count;
  var latest=await unread.Include(n=>n.Change).OrderByDescending(n=>n.Id).Select(n=>new {n.Id,n.Change.DoctorName}).FirstOrDefaultAsync();
  return View(new NotificationNavModel(count,latest?.Id,latest?.DoctorName));
 }
}
