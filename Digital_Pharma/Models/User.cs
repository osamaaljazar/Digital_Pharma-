using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Digital_Pharma.Models
{
    public class User
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserName { get; set; } = string.Empty;

        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;

        public string? PasswordHash { get; set; }

        public string? PhoneNumber { get; set; }

        [Required]
        public DateTime DateOfBirth { get; set; }

        // Patient Profile
        public string? Gender { get; set; }             // "male" or "female"
        public string? MedicalConditions { get; set; }  // comma-separated or free text
        public bool? IsMarried { get; set; }            // null = not answered
        public bool? IsPregnant { get; set; }           // null = not applicable
        public bool ProfileCompleted { get; set; } = false;

        public bool EmailConfirmed { get; set; } = false;
        public bool TwoFactorEnabled { get; set; } = false;
        public DateTimeOffset? LockoutEnd { get; set; }

        [NotMapped]
        public int Age => DateTime.Today.Year - DateOfBirth.Year -
                          (DateTime.Today.DayOfYear < DateOfBirth.DayOfYear ? 1 : 0);

        public ICollection<Search> Searches { get; set; } = new List<Search>();
    }
}
