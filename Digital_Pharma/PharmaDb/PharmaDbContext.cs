using Microsoft.EntityFrameworkCore;
using Digital_Pharma.Models;

namespace DB.DBB
{
    public class PharmaDbContext : DbContext
    {
        public PharmaDbContext(DbContextOptions<PharmaDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<Doctor> Doctors { get; set; }
        public DbSet<Search> Searches { get; set; }
        public DbSet<UserMedication> UserMedications { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

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
        }
    }
}
