using System.ComponentModel.DataAnnotations;
namespace Digital_Pharma.Models;
public sealed class DoctorApplicationInput
{
    [Required(ErrorMessage="الاسم مطلوب"), StringLength(150)] public string FullName { get; set; } = "";
    [Required(ErrorMessage="رقم الترخيص مطلوب"), StringLength(100)] public string LicenseNumber { get; set; } = "";
    [Required(ErrorMessage="التخصص مطلوب"), StringLength(150)] public string Specialization { get; set; } = "";
    [Required(ErrorMessage="البلد مطلوب"), StringLength(100)] public string Country { get; set; } = "";
    [Required(ErrorMessage="جهة الترخيص مطلوبة"), StringLength(200)] public string LicensingAuthority { get; set; } = "";
    [Required(ErrorMessage="رقم التواصل مطلوب"), StringLength(30), Phone] public string ContactPhone { get; set; } = "";
    public string? Version { get; set; }
}

