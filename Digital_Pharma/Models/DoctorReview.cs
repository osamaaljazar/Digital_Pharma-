using System.ComponentModel.DataAnnotations;
namespace Digital_Pharma.Models;
public enum DoctorApplicationStatus { Pending, NeedsInformation, Approved, Rejected, Suspended }
public class DoctorReview
{
    public int Id { get; set; }
    public int DoctorId { get; set; }
    public Doctor Doctor { get; set; } = null!;
    public int ActorUserId { get; set; }
    public User ActorUser { get; set; } = null!;
    public DoctorApplicationStatus Status { get; set; }
    [MaxLength(2000)] public string Note { get; set; } = "";
    public string ApplicationSnapshot { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public class DoctorReviewInput
{
    public int Id { get; set; }
    [Required] public string Version { get; set; } = "";
    public DoctorApplicationStatus Decision { get; set; }
    [Required(ErrorMessage="يرجى كتابة سبب القرار وطريقة التحقق"), StringLength(2000)] public string Note { get; set; } = "";
    public bool CredentialsChecked { get; set; }
}

