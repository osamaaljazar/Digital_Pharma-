using System.ComponentModel.DataAnnotations;
using Digital_Pharma.Services;

namespace Digital_Pharma.Models;

public sealed class MedicationDashboard
{
    public List<UserMedication> Medications { get; set; } = [];
    public int TakenLast7Days { get; set; }
    public int SkippedLast7Days { get; set; }
    public decimal? RecordedAdherencePercent { get; set; }
    public int LowStockCount => Medications.Count(m => !m.IsStopped && MedicationInsights.StockState(m) is "low" or "empty");
    public int ExpiryAlertCount => Medications.Count(m => !m.IsStopped && MedicationInsights.ExpiryState(m) is "soon" or "expired");
}

public sealed class RestockInput
{
    public int Id { get; set; }
    [Required] public string Version { get; set; } = "";
    [Range(typeof(decimal), "0.001", "999999.999")] public decimal Amount { get; set; }
    [Required] public string Unit { get; set; } = "";
    [Required, StringLength(500)] public string Reason { get; set; } = "شراء عبوة جديدة";
}

public static class MedicationInsights
{
    public static bool CanDeduct(UserMedication medication) =>
        medication.Quantity.HasValue && medication.DoseAmount.HasValue &&
        !string.IsNullOrWhiteSpace(medication.QuantityUnit) &&
        string.Equals(medication.QuantityUnit, medication.DoseUnit, StringComparison.Ordinal);

    public static decimal? DailyUse(UserMedication medication)
    {
        if (!CanDeduct(medication) || medication.ScheduleKind != "daily") return null;
        var count = MedicationCardInput.Times(medication.DailyTimes).Length;
        return count == 0 ? null : medication.DoseAmount!.Value * count;
    }

    public static decimal? RemainingDays(UserMedication medication)
    {
        var daily = DailyUse(medication);
        return daily > 0 && medication.Quantity.HasValue
            ? decimal.Round(medication.Quantity.Value / daily.Value, 1)
            : null;
    }

    public static string StockState(UserMedication medication)
    {
        if (!medication.Quantity.HasValue) return "unknown";
        if (medication.Quantity.Value <= 0) return "empty";
        var days = RemainingDays(medication);
        if (days.HasValue && days.Value <= 3) return "low";
        if (!days.HasValue && medication.Quantity.Value <= 5) return "low";
        return "ok";
    }

    public static string ExpiryState(UserMedication medication)
    {
        if (!medication.ExpiryDate.HasValue) return "unknown";
        var days = (medication.ExpiryDate.Value.Date - DoseSchedule.Now.Date).Days;
        return days < 0 ? "expired" : days <= 30 ? "soon" : "ok";
    }
}
