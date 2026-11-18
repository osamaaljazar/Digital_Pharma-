using DB.DBB;
using Digital_Pharma.Models;
using Microsoft.EntityFrameworkCore;
namespace Digital_Pharma.Services;
public sealed class HealthPageService(PharmaDbContext db,HealthReviewEngine engine) {
 public async Task<HealthPage> LoadAsync(int userId) {
  var user=await db.Users.AsNoTracking().SingleAsync(u=>u.Id==userId);
  var conditions=await db.PatientConditions.AsNoTracking().Where(c=>c.UserId==userId).ToListAsync();
  var allergies=await db.PatientAllergies.AsNoTracking().Where(a=>a.UserId==userId).OrderBy(a=>a.Id).ToListAsync();
  var meds=await db.UserMedications.AsNoTracking().Where(m=>m.UserId==userId).ToListAsync();
  return new HealthPage { Profile=new HealthProfileInput {UserName=user.UserName,DateOfBirth=user.DateOfBirth,Gender=user.Gender??"unknown",
   IsPregnant=user.IsPregnant,ConditionCodes=conditions.Select(c=>c.Code).ToList(),OtherNotes=user.HealthNotes,Version=Convert.ToBase64String(user.HealthRowVersion)},
   Allergies=allergies,Medications=meds,SubstanceOptions=HealthReviewEngine.Substances(meds),
   Review=engine.Evaluate(user,conditions,allergies,meds)};
 }
}

