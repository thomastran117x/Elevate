using System.Text.Json;

using backend.main.features.auth.device;
using backend.main.features.clubs;
using backend.main.features.clubs.follow;
using backend.main.features.clubs.posts;
using backend.main.features.clubs.posts.comments;
using backend.main.features.clubs.reviews;
using backend.main.features.clubs.staff;
using backend.main.features.clubs.versions;
using backend.main.features.events;
using backend.main.features.events.images;
using backend.main.features.events.registration;
using backend.main.features.events.search;
using backend.main.features.events.versions;
using backend.main.features.payment;
using backend.main.features.profile;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace backend.main.infrastructure.database.core
{
    public class AppDatabaseContext : DbContext
    {
        public DbSet<User> Users { get; set; } = null!;
        public DbSet<Club> Clubs { get; set; } = null!;
        public DbSet<ClubStaff> ClubStaff { get; set; } = null!;
        public DbSet<ClubVersion> ClubVersions { get; set; } = null!;
        public DbSet<Events> Events { get; set; } = null!;
        public DbSet<EventVersion> EventVersions { get; set; } = null!;
        public DbSet<FollowClub> FollowClubs { get; set; } = null!;
        public DbSet<Payment> Payments { get; set; } = null!;
        public DbSet<ClubReview> ClubReviews { get; set; } = null!;
        public DbSet<Device> Devices { get; set; } = null!;
        public DbSet<ClubPost> ClubPosts { get; set; } = null!;
        public DbSet<PostComment> PostComments { get; set; } = null!;
        public DbSet<EventRegistration> EventRegistrations { get; set; } = null!;
        public DbSet<EventImage> EventImages { get; set; } = null!;
        public DbSet<EventSearchOutbox> EventSearchOutbox { get; set; } = null!;
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

            modelBuilder.Entity<User>()
                .Property(u => u.IsDisabled)
                .HasDefaultValue(false);

            modelBuilder.Entity<User>()
                .Property(u => u.AuthVersion)
                .HasDefaultValue(1);

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

            modelBuilder.Entity<ClubStaff>()
                .HasOne<Club>()
                .WithMany()
                .HasForeignKey(cs => cs.ClubId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ClubStaff>()
                .HasOne<User>()
                .WithMany()
                .HasForeignKey(cs => cs.UserId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ClubStaff>()
                .HasOne<User>()
                .WithMany()
                .HasForeignKey(cs => cs.GrantedByUserId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ClubStaff>()
                .Property(cs => cs.Role)
                .HasConversion<string>()
                .HasMaxLength(32);

            modelBuilder.Entity<ClubStaff>()
                .HasIndex(cs => new { cs.ClubId, cs.UserId })
                .IsUnique();

            modelBuilder.Entity<ClubStaff>()
                .HasIndex(cs => cs.UserId);

            modelBuilder.Entity<ClubVersion>()
                .HasOne<Club>()
                .WithMany()
                .HasForeignKey(v => v.ClubId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ClubVersion>()
                .Property(v => v.ActionType)
                .HasMaxLength(32);

            modelBuilder.Entity<ClubVersion>()
                .Property(v => v.ActorRole)
                .HasMaxLength(64);

            modelBuilder.Entity<ClubVersion>()
                .HasIndex(v => new { v.ClubId, v.VersionNumber })
                .IsUnique();

            modelBuilder.Entity<ClubVersion>()
                .HasIndex(v => v.CreatedAt);

            modelBuilder.Entity<ClubVersion>()
                .HasIndex(v => v.ClubImage);

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

            var tagsComparer = new ValueComparer<List<string>>(
                (a, b) => (a ?? new List<string>()).SequenceEqual(b ?? new List<string>()),
                v => v.Aggregate(0, (acc, t) => HashCode.Combine(acc, t.GetHashCode())),
                v => v.ToList());

            modelBuilder.Entity<Events>()
                .Property(e => e.Tags)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => string.IsNullOrEmpty(v)
                        ? new List<string>()
                        : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
                .HasColumnType("json")
                .Metadata.SetValueComparer(tagsComparer);

            modelBuilder.Entity<Events>()
                .Property(e => e.VenueName)
                .HasMaxLength(100);

            modelBuilder.Entity<Events>()
                .Property(e => e.City)
                .HasMaxLength(100);

            modelBuilder.Entity<Events>()
                .HasIndex(e => e.Category);

            modelBuilder.Entity<Events>()
                .HasIndex(e => e.City);

            modelBuilder.Entity<Events>()
                .HasIndex(e => new { e.Latitude, e.Longitude });

            modelBuilder.Entity<EventVersion>()
                .HasOne<Events>()
                .WithMany()
                .HasForeignKey(v => v.EventId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<EventVersion>()
                .Property(v => v.ActionType)
                .HasMaxLength(32);

            modelBuilder.Entity<EventVersion>()
                .Property(v => v.ActorRole)
                .HasMaxLength(64);

            modelBuilder.Entity<EventVersion>()
                .HasIndex(v => new { v.EventId, v.VersionNumber })
                .IsUnique();

            modelBuilder.Entity<EventVersion>()
                .HasIndex(v => v.CreatedAt);

            modelBuilder.Entity<EventImage>()
                .HasOne(ei => ei.Event)
                .WithMany(e => e.Images)
                .HasForeignKey(ei => ei.EventId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<EventImage>()
                .HasIndex(ei => ei.EventId);

            modelBuilder.Entity<EventImage>()
                .HasIndex(ei => new { ei.EventId, ei.SortOrder });

            modelBuilder.Entity<Payment>()
                .HasOne<User>()
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Payment>()
                .HasOne<Events>()
                .WithMany()
                .HasForeignKey(p => p.EventId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Payment>()
                .HasIndex(p => p.UserId);

            modelBuilder.Entity<Payment>()
                .HasIndex(p => p.EventId);

            modelBuilder.Entity<Payment>()
                .HasIndex(p => p.ExternalSessionId)
                .IsUnique();

            modelBuilder.Entity<Payment>()
                .HasIndex(p => p.IdempotencyKey)
                .IsUnique();

            modelBuilder.Entity<ClubReview>()
                .HasOne<User>()
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ClubReview>()
                .HasOne<Club>()
                .WithMany()
                .HasForeignKey(r => r.ClubId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ClubReview>()
                .HasIndex(r => r.ClubId);

            modelBuilder.Entity<ClubReview>()
                .HasIndex(r => r.UserId);

            modelBuilder.Entity<Device>()
                .HasOne<User>()
                .WithMany()
                .HasForeignKey(d => d.UserId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Device>()
                .HasIndex(d => d.DeviceTokenHash)
                .IsUnique();

            modelBuilder.Entity<Device>()
                .HasIndex(d => d.UserId);

            modelBuilder.Entity<Device>()
                .HasIndex(d => new { d.UserId, d.DeviceType, d.ClientName });

            modelBuilder.Entity<ClubPost>()
                .HasOne<User>()
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ClubPost>()
                .HasOne<Club>()
                .WithMany()
                .HasForeignKey(p => p.ClubId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ClubPost>()
                .HasIndex(p => p.ClubId);

            modelBuilder.Entity<ClubPost>()
                .HasIndex(p => p.UserId);

            modelBuilder.Entity<PostComment>()
                .HasOne<ClubPost>()
                .WithMany()
                .HasForeignKey(c => c.PostId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PostComment>()
                .HasOne<User>()
                .WithMany()
                .HasForeignKey(c => c.UserId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PostComment>()
                .HasIndex(c => c.PostId);

            modelBuilder.Entity<PostComment>()
                .HasIndex(c => c.UserId);

            modelBuilder.Entity<EventRegistration>()
                .HasOne<User>()
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<EventRegistration>()
                .HasOne<Events>()
                .WithMany()
                .HasForeignKey(r => r.EventId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<EventRegistration>()
                .HasIndex(r => r.EventId);

            modelBuilder.Entity<EventRegistration>()
                .HasIndex(r => r.UserId);

            modelBuilder.Entity<EventRegistration>()
                .HasIndex(r => new { r.EventId, r.UserId })
                .IsUnique();

            modelBuilder.Entity<EventSearchOutbox>()
                .ToTable("event_search_outbox");

            modelBuilder.Entity<EventSearchOutbox>()
                .Property(e => e.Id)
                .HasColumnName("id");

            modelBuilder.Entity<EventSearchOutbox>()
                .Property(e => e.AggregateType)
                .HasColumnName("aggregatetype")
                .HasMaxLength(255);

            modelBuilder.Entity<EventSearchOutbox>()
                .Property(e => e.AggregateId)
                .HasColumnName("aggregateid")
                .HasMaxLength(255);

            modelBuilder.Entity<EventSearchOutbox>()
                .Property(e => e.Type)
                .HasColumnName("type")
                .HasMaxLength(255);

            modelBuilder.Entity<EventSearchOutbox>()
                .Property(e => e.Payload)
                .HasColumnName("payload")
                .HasColumnType("json");

            modelBuilder.Entity<EventSearchOutbox>()
                .Property(e => e.CreatedAt)
                .HasColumnName("created_at");

            modelBuilder.Entity<EventSearchOutbox>()
                .HasIndex(e => e.CreatedAt);
        }
    }
}
