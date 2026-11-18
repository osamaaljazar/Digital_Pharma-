namespace Digital_Pharma.Models;

public sealed record SystemNotification(string Kind,string Title,string Message,string ActionController,string Action,string Priority);

public sealed class NotificationCenterPage
{
    public List<SystemNotification> CurrentAlerts { get; set; } = [];
    public List<PatientNotification> TreatmentNotifications { get; set; } = [];
    public int Page { get; set; } = 1;
}
