using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Digital_Pharma.Models
{
    public class Doctor
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string FullName { get; set; } = "";

        [Required]
        public string LicenseNumber { get; set; } = "";

        [Required]
        public string Specialization { get; set; } = "";

        public bool IsVerified { get; set; } = false;

        public DoctorApplicationStatus Status { get; set; } = DoctorApplicationStatus.Pending;
        [MaxLength(100)] public string Country { get; set; } = "";
        [MaxLength(200)] public string LicensingAuthority { get; set; } = "";
        [MaxLength(30)] public string ContactPhone { get; set; } = "";
        public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;
        [Timestamp] public byte[] RowVersion { get; set; } = Array.Empty<byte>();
        public ICollection<DoctorReview> Reviews { get; set; } = new List<DoctorReview>();

        // ربط مع User
        public int UserId { get; set; }

        [ForeignKey("UserId")]
        public User? User { get; set; } // أصبح nullable لتجنب warnings
    }
}
