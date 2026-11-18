using DB.DBB;
using Digital_Pharma.Models;
using Microsoft.EntityFrameworkCore;

namespace Digital_Pharma.Services;

public sealed class PatientAlertService(PharmaDbContext db)
{
    public async Task<List<SystemNotification>> CurrentAsync(int userId)
    {
        var now = DoseSchedule.Now;
        var meds = await db.UserMedications.AsNoTracking().Where(m => m.UserId == userId && !m.IsStopped).ToListAsync();
        var records = await db.DoseRecords.AsNoTracking().Where(d => d.UserId == userId && d.Day == now.Date).Select(d => new { d.MedicationId, d.Time }).ToListAsync();
        var recorded = records.Select(r => (r.MedicationId, r.Time)).ToHashSet();
        var alerts = new List<SystemNotification>();

        foreach (var med in meds)
        {
            var stock = MedicationInsights.StockState(med);
            if (stock == "empty") alerts.Add(new("stock", "نفد مخزون الدواء", $"الكمية المسجلة لدواء {med.DrugName} وصلت إلى الصفر.", "Home", "MyMedications", "urgent"));
            else if (stock == "low") alerts.Add(new("stock", "مخزون منخفض", $"الكمية المسجلة لدواء {med.DrugName} منخفضة. راجع الكمية قبل نفادها.", "Home", "MyMedications", "review"));

            var expiry = MedicationInsights.ExpiryState(med);
            if (expiry == "expired") alerts.Add(new("expiry", "دواء منتهي الصلاحية", $"تاريخ الصلاحية المسجل لدواء {med.DrugName} انتهى. لا تستخدمه قبل سؤال الصيدلي.", "Home", "MyMedications", "urgent"));
            else if (expiry == "soon") alerts.Add(new("expiry", "صلاحية قريبة", $"اقترب تاريخ الصلاحية المسجل لدواء {med.DrugName}.", "Home", "MyMedications", "review"));

            foreach (var time in DoseSchedule.Times(med).Where(t => string.CompareOrdinal(t, now.ToString("HH:mm")) <= 0 && !recorded.Contains((med.Id, t))))
                alerts.Add(new("dose", "جرعة مستحقة غير مسجلة", $"جرعة {med.DrugName} المجدولة عند {time} لم تُسجل بعد.", "Doses", "Index", "review"));
        }

        return alerts.OrderBy(a => a.Priority == "urgent" ? 0 : 1).ThenBy(a => a.Kind).ThenBy(a => a.Title).ToList();
    }
}
