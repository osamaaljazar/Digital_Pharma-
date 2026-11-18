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

        [MaxLength(50)] public string? LabelId { get; set; }
        public string? LabelJson { get; set; }
        public DateTimeOffset? LabelRetrievedAt { get; set; }

        [Column(TypeName="decimal(12,3)")] public decimal? DoseAmount { get; set; }
        [MaxLength(30)] public string? DoseUnit { get; set; }
        [MaxLength(30)] public string ScheduleKind { get; set; } = "unspecified";
        [MaxLength(200)] public string? DailyTimes { get; set; }
        [Column(TypeName="decimal(12,3)")] public decimal? Quantity { get; set; }
        [MaxLength(30)] public string? QuantityUnit { get; set; }
        [DataType(DataType.Date)] public DateTime? ExpiryDate { get; set; }
        [MaxLength(1000)] public string? Instructions { get; set; }
        public bool IsStopped { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
        [Timestamp] public byte[] RowVersion { get; set; } = [];

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public int UserId { get; set; }
        [ForeignKey("UserId")]
        public User? User { get; set; }
    }
}
