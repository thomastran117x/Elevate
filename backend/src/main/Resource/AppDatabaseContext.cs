using backend.main.Models;

using Microsoft.EntityFrameworkCore;

namespace backend.main.Resources
{
    public class AppDatabaseContext : DbContext
    {
        public DbSet<User> Users { get; set; } = null!;
        public DbSet<Club> Clubs { get; set; } = null!;
        public DbSet<Events> Events { get; set; } = null!;
        public DbSet<FollowClub> FollowClubs { get; set; } = null!;
        public AppDatabaseContext(DbContextOptions<AppDatabaseContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            modelBuilder.Entity<User>()
                .HasIndex(u => u.Username)
                .IsUnique();

            modelBuilder.Entity<User>()
                .HasIndex(u => u.GoogleID)
                .IsUnique();

            modelBuilder.Entity<User>()
                .HasIndex(u => u.MicrosoftID)
                .IsUnique();

            modelBuilder.Entity<Club>()
                .HasOne<User>()
                .WithMany()
                .HasForeignKey(c => c.UserId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Club>()
                .Property(c => c.Rating)
                .HasPrecision(2, 1);

            modelBuilder.Entity<Club>()
                .HasIndex(c => c.UserId);

            modelBuilder.Entity<FollowClub>()
                .HasOne<User>()
                .WithMany()
                .HasForeignKey(f => f.UserId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<FollowClub>()
                .HasOne<Club>()
                .WithMany()
                .HasForeignKey(f => f.ClubId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<FollowClub>()
                .HasIndex(f => f.ClubId);

            modelBuilder.Entity<FollowClub>()
                .HasIndex(f => f.UserId);

            modelBuilder.Entity<Events>()
                .HasOne<Club>()
                .WithMany()
                .HasForeignKey(c => c.ClubId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
