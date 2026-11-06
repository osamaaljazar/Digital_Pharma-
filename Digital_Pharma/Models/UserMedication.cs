using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Digital_Pharma.Models
{
    public class UserMedication
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string DrugName { get; set; } = string.Empty;

        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public int UserId { get; set; }
        [ForeignKey("UserId")]
        public User? User { get; set; }
    }
}
