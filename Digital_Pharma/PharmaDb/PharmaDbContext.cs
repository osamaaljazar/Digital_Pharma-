using Microsoft.EntityFrameworkCore;
using Digital_Pharma.Models;

namespace DB.DBB
{
    public class PharmaDbContext : DbContext
    {
        private readonly EncryptedStringConverter _encrypted;
        private readonly NullableEncryptedStringConverter _nullableEncrypted;

        public PharmaDbContext(DbContextOptions<PharmaDbContext> options, Digital_Pharma.Services.FieldEncryptionService encryption) : base(options)
        {
            _encrypted = new EncryptedStringConverter(encryption);
            _nullableEncrypted = new NullableEncryptedStringConverter(encryption);
        }

        public DbSet<TreatmentChange> TreatmentChanges {get;set;}
        public DbSet<PatientNotification> PatientNotifications {get;set;}
        public DbSet<CareLink> CareLinks {get;set;}
        public DbSet<CareLinkEvent> CareLinkEvents {get;set;}
        public DbSet<ClinicalAlertDecision> ClinicalAlertDecisions {get;set;}
        public DbSet<AdminAccountEvent> AdminAccountEvents {get;set;}
        public DbSet<DoseRecord> DoseRecords {get;set;}
        public DbSet<DoseChange> DoseChanges {get;set;}
        public DbSet<User> Users { get; set; }
        public DbSet<PasswordResetToken> PasswordResetTokens { get; set; }
        public DbSet<EmailConfirmationToken> EmailConfirmationTokens { get; set; }
        public DbSet<Doctor> Doctors { get; set; }
        public DbSet<Search> Searches { get; set; }
        public DbSet<UserMedication> UserMedications { get; set; }

        public DbSet<MedicationChange> MedicationChanges { get; set; }

        public DbSet<PatientCondition> PatientConditions { get; set; }
        public DbSet<PatientAllergy> PatientAllergies { get; set; }
        public DbSet<DoctorReview> DoctorReviews { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<PasswordResetToken>().HasIndex(t => t.TokenHash).IsUnique();
            modelBuilder.Entity<EmailConfirmationToken>().HasIndex(t => t.TokenHash).IsUnique();
            modelBuilder.Entity<Search>().HasIndex(s=>new{s.UserId,s.CheckGroupId});
            modelBuilder.Entity<Search>().HasIndex(s=>s.ShareTokenHash).IsUnique().HasFilter("[ShareTokenHash] IS NOT NULL");
            modelBuilder.Entity<PasswordResetToken>().HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<EmailConfirmationToken>().HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<TreatmentChange>().HasIndex(c=>c.OperationId).IsUnique();
            modelBuilder.Entity<TreatmentChange>().HasOne<User>().WithMany().HasForeignKey(c=>c.PatientId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<PatientNotification>().HasOne(n=>n.Change).WithMany().HasForeignKey(n=>n.TreatmentChangeId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<PatientNotification>().HasIndex(n=>n.TreatmentChangeId).IsUnique();
            modelBuilder.Entity<CareLink>().HasIndex(l=>new{l.PatientId,l.DoctorId}).IsUnique();
            modelBuilder.Entity<CareLink>().HasOne(l=>l.Patient).WithMany().HasForeignKey(l=>l.PatientId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<CareLink>().HasOne(l=>l.Doctor).WithMany().HasForeignKey(l=>l.DoctorId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<CareLinkEvent>().HasOne(e=>e.Link).WithMany().HasForeignKey(e=>e.CareLinkId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<ClinicalAlertDecision>().HasIndex(d=>d.OperationId).IsUnique();
            modelBuilder.Entity<ClinicalAlertDecision>().HasIndex(d=>new{d.CareLinkId,d.AlertKey,d.CreatedAt});
            modelBuilder.Entity<ClinicalAlertDecision>().HasOne(d=>d.Link).WithMany().HasForeignKey(d=>d.CareLinkId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<ClinicalAlertDecision>().HasOne<User>().WithMany().HasForeignKey(d=>d.PatientId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<AdminAccountEvent>().HasIndex(e=>new{e.TargetUserId,e.CreatedAt});
            modelBuilder.Entity<AdminAccountEvent>().HasOne<User>().WithMany().HasForeignKey(e=>e.TargetUserId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<DoseRecord>().HasIndex(d=>new {d.UserId,d.MedicationId,d.Day,d.Time}).IsUnique();
            modelBuilder.Entity<DoseRecord>().HasOne<User>().WithMany().HasForeignKey(d=>d.UserId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<DoseChange>().HasOne(c=>c.Record).WithMany().HasForeignKey(c=>c.DoseRecordId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<MedicationChange>().HasOne(c=>c.Medication).WithMany().HasForeignKey(c=>c.UserMedicationId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<PatientCondition>().HasOne(c=>c.User).WithMany().HasForeignKey(c=>c.UserId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<PatientCondition>().HasIndex(c=>new {c.UserId,c.Code}).IsUnique();
            modelBuilder.Entity<PatientAllergy>().HasOne(c=>c.User).WithMany().HasForeignKey(c=>c.UserId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<UserMedication>().HasIndex(m=>new {m.UserId,m.LabelId}).IsUnique().HasFilter("[LabelId] IS NOT NULL");
            modelBuilder.Entity<DoctorReview>().HasOne(r=>r.Doctor).WithMany(d=>d.Reviews)
                .HasForeignKey(r=>r.DoctorId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<DoctorReview>().HasOne(r=>r.ActorUser).WithMany()
                .HasForeignKey(r=>r.ActorUserId).OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<User>()
                .HasMany(u => u.Searches)
                .WithOne(s => s.User)
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Doctor>()
                .HasOne(d => d.User)
                .WithOne()
                .HasForeignKey<Doctor>(d => d.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Search>()
                .HasOne(s => s.Doctor)
                .WithMany()
                .HasForeignKey(s => s.DoctorId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<UserMedication>()
                .HasOne(m => m.User)
                .WithMany()
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Authenticated encryption at rest. Do not add properties used by SQL filtering,
            // ordering or uniqueness unless a separate lookup hash/index is introduced first.
            modelBuilder.Entity<User>().Property(u => u.PhoneNumber).HasConversion(_nullableEncrypted).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<User>().Property(u => u.MedicalConditions).HasConversion(_nullableEncrypted).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<User>().Property(u => u.HealthNotes).HasConversion(_nullableEncrypted).HasColumnType("nvarchar(max)");

            modelBuilder.Entity<Search>().Property(s => s.Drug1Name).HasConversion(_encrypted).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<Search>().Property(s => s.Drug2Name).HasConversion(_encrypted).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<Search>().Property(s => s.ResultMessage).HasConversion(_nullableEncrypted).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<Search>().Property(s => s.AiAnalysis).HasConversion(_nullableEncrypted).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<Search>().Property(s => s.Recommendation).HasConversion(_nullableEncrypted).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<Search>().Property(s => s.PatientContext).HasConversion(_nullableEncrypted).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<Search>().Property(s => s.SafeAlternatives).HasConversion(_nullableEncrypted).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<Search>().Property(s => s.EvidenceNote).HasConversion(_nullableEncrypted).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<Search>().Property(s => s.SourceSummary).HasConversion(_nullableEncrypted).HasColumnType("nvarchar(max)");

            modelBuilder.Entity<PatientAllergy>().Property(a => a.Name).HasConversion(_encrypted).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<PatientAllergy>().Property(a => a.SubstanceKey).HasConversion(_nullableEncrypted).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<PatientAllergy>().Property(a => a.Reaction).HasConversion(_encrypted).HasColumnType("nvarchar(max)");

            modelBuilder.Entity<UserMedication>().Property(m => m.DrugName).HasConversion(_encrypted).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<UserMedication>().Property(m => m.Notes).HasConversion(_nullableEncrypted).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<UserMedication>().Property(m => m.LabelJson).HasConversion(_nullableEncrypted).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<UserMedication>().Property(m => m.DailyTimes).HasConversion(_nullableEncrypted).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<UserMedication>().Property(m => m.Instructions).HasConversion(_nullableEncrypted).HasColumnType("nvarchar(max)");

            modelBuilder.Entity<MedicationChange>().Property(c => c.Reason).HasConversion(_encrypted).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<MedicationChange>().Property(c => c.BeforeJson).HasConversion(_encrypted).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<MedicationChange>().Property(c => c.AfterJson).HasConversion(_encrypted).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<DoseRecord>().Property(d => d.DrugName).HasConversion(_encrypted).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<DoseChange>().Property(c => c.Reason).HasConversion(_encrypted).HasColumnType("nvarchar(max)");

            modelBuilder.Entity<Doctor>().Property(d => d.LicenseNumber).HasConversion(_encrypted).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<Doctor>().Property(d => d.LicensingAuthority).HasConversion(_encrypted).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<Doctor>().Property(d => d.ContactPhone).HasConversion(_encrypted).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<DoctorReview>().Property(r => r.Note).HasConversion(_encrypted).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<DoctorReview>().Property(r => r.ApplicationSnapshot).HasConversion(_encrypted).HasColumnType("nvarchar(max)");

            modelBuilder.Entity<TreatmentChange>().Property(c => c.DoctorName).HasConversion(_encrypted).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<TreatmentChange>().Property(c => c.Reason).HasConversion(_encrypted).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<TreatmentChange>().Property(c => c.BeforeJson).HasConversion(_nullableEncrypted).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<TreatmentChange>().Property(c => c.AfterJson).HasConversion(_encrypted).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<ClinicalAlertDecision>().Property(d => d.Reason).HasConversion(_encrypted).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<ClinicalAlertDecision>().Property(d => d.AlertSnapshot).HasConversion(_encrypted).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<AdminAccountEvent>().Property(e => e.Reason).HasConversion(_encrypted).HasColumnType("nvarchar(max)");
        }
    }
}
