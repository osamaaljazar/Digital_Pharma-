using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Digital_Pharma.Models
{
    public class Doctor
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string FullName { get; set; }

        [Required]
        public string LicenseNumber { get; set; }

        [Required]
        public string Specialization { get; set; }

        public bool IsVerified { get; set; } = false;

        // ربط مع User
        public int UserId { get; set; }

        [ForeignKey("UserId")]
        public User? User { get; set; } // أصبح nullable لتجنب warnings
    }
}
