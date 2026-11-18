using System.Globalization;
using Digital_Pharma.Models;
namespace Digital_Pharma.Services;
public static class DoseSchedule {
 public static DateTimeOffset Now=>TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,TimeZoneInfo.FindSystemTimeZoneById("Asia/Amman"));
 public static string[] Times(UserMedication m)=>m.IsStopped || m.ScheduleKind!="daily" || m.DoseAmount is not >0 || !MedicationCardInput.Units.ContainsKey(m.DoseUnit??"") ? [] :
  MedicationCardInput.Times(m.DailyTimes).Where(t=>TimeOnly.TryParseExact(t,"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out _)).Distinct().Order().ToArray();
}
