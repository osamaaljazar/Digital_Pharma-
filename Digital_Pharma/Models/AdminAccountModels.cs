using System.ComponentModel.DataAnnotations;

namespace Digital_Pharma.Models;

public sealed class AdminAccountsPage
{
    public List<User> Users { get; set; } = [];
    public Dictionary<int, DoctorApplicationStatus> DoctorStatuses { get; set; } = [];
    public string Query { get; set; } = "";
    public string State { get; set; } = "all";
    public int Page { get; set; } = 1;
    public bool HasNext { get; set; }
}

public class AdminAccountEvent
{
    public int Id { get; set; }
    public int TargetUserId { get; set; }
    public int ActorUserId { get; set; }
    [MaxLength(30)] public string Action { get; set; } = "";
    [MaxLength(500)] public string Reason { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
