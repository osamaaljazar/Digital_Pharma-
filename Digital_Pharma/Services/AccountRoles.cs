using System.Security.Claims;
using DB.DBB;
using Digital_Pharma.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
namespace Digital_Pharma.Services;
public static class AccountRoles
{
    public const string Patient = "Patient", Applicant = "DoctorApplicant", Doctor = "Doctor", Admin = "Admin";
    public static async Task<ClaimsPrincipal> PrincipalAsync(User user, PharmaDbContext db)
    {
        var application = await db.Doctors.AsNoTracking().SingleOrDefaultAsync(d => d.UserId == user.Id);
        var role = user.IsAdmin ? Admin :
            application?.Status == DoctorApplicationStatus.Approved && application.IsVerified ? Doctor :
            user.IsDoctorApplicant || application != null ? Applicant : Patient;
        return new ClaimsPrincipal(new ClaimsIdentity(new[] {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.UserName), new Claim(ClaimTypes.Role, role),
            new Claim("session_version", user.SessionVersion.ToString())
        }, CookieAuthenticationDefaults.AuthenticationScheme));
    }
}

