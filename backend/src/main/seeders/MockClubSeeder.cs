using backend.main.configurations.resource.database;
using backend.main.models.core;
using backend.main.models.enums;

using Microsoft.EntityFrameworkCore;

namespace backend.main.seeders
{
    public sealed class MockClubSeeder : ISeeder
    {
        private readonly AppDatabaseContext _dbContext;
        private readonly ILogger<MockClubSeeder> _logger;

        public MockClubSeeder(
            AppDatabaseContext dbContext,
            ILogger<MockClubSeeder> logger
        )
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task SeedAsync(CancellationToken cancellationToken = default)
        {
            var seedClubs = BuildSeedClubs();
            var ownerEmails = seedClubs
                .Select(club => club.OwnerEmail)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var ownerList = await _dbContext.Users
                .Where(user => ownerEmails.Contains(user.Email))
                .ToListAsync(cancellationToken);
            var owners = ownerList.ToDictionary(user => user.Email, StringComparer.OrdinalIgnoreCase);

            var missingOwners = ownerEmails
                .Where(email => !owners.ContainsKey(email))
                .ToList();

            if (missingOwners.Count > 0)
            {
                _logger.LogWarning(
                    "[Seeders] Skipping mock clubs because required owner users are missing: {Emails}",
                    string.Join(", ", missingOwners)
                );
                return;
            }

            var clubNames = seedClubs
                .Select(club => club.Name)
                .ToList();

            var existingClubNames = await _dbContext.Clubs
                .AsNoTracking()
                .Where(club => clubNames.Contains(club.Name))
                .Select(club => club.Name)
                .ToListAsync(cancellationToken);
            var existingLookup = existingClubNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

            var missingClubs = new List<Club>();

            foreach (var seedClub in seedClubs)
            {
                if (existingLookup.Contains(seedClub.Name))
                    continue;

                var owner = owners[seedClub.OwnerEmail];

                missingClubs.Add(new Club
                {
                    Name = seedClub.Name,
                    Description = seedClub.Description,
                    Clubtype = seedClub.ClubType,
                    ClubImage = seedClub.ClubImage,
                    Phone = seedClub.Phone,
                    Email = seedClub.Email,
                    WebsiteUrl = seedClub.WebsiteUrl,
                    Location = seedClub.Location,
                    MaxMemberCount = seedClub.MaxMemberCount,
                    isPrivate = false,
                    UserId = owner.Id,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }

            if (missingClubs.Count == 0)
            {
                _logger.LogInformation("[Seeders] Mock clubs already present. No new clubs added.");
                return;
            }

            await _dbContext.Clubs.AddRangeAsync(missingClubs, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "[Seeders] Seeded {Count} mock club records.",
                missingClubs.Count
            );
        }

        private static List<SeedClubDefinition> BuildSeedClubs()
        {
            return
            [
                new SeedClubDefinition(
                    "local.user02@seed.eventxperience.test",
                    "Harbour Runners Club",
                    "A friendly Toronto running club focused on beginner distance training and waterfront meetups.",
                    ClubType.Sports,
                    "https://placehold.co/600x400?text=Harbour+Runners",
                    "harbour.runners@seed.eventxperience.test",
                    "+1-416-555-0101",
                    "https://seed.eventxperience.test/harbour-runners",
                    "Toronto Waterfront",
                    250
                ),
                new SeedClubDefinition(
                    "local.user05@seed.eventxperience.test",
                    "North Campus Builders",
                    "Student organizers hosting startup, product, and founder events across campus.",
                    ClubType.Academic,
                    "https://placehold.co/600x400?text=North+Campus+Builders",
                    "builders@seed.eventxperience.test",
                    "+1-416-555-0102",
                    "https://seed.eventxperience.test/north-campus-builders",
                    "Downtown Campus",
                    400
                ),
                new SeedClubDefinition(
                    "local.user08@seed.eventxperience.test",
                    "Lantern Social Collective",
                    "A social club for low-pressure mixers, hobby nights, and community pop-up gatherings.",
                    ClubType.Social,
                    "https://placehold.co/600x400?text=Lantern+Social",
                    "hello@lanternsocial.test",
                    "+1-416-555-0103",
                    "https://seed.eventxperience.test/lantern-social",
                    "West End Toronto",
                    300
                )
            ];
        }

        private sealed record SeedClubDefinition(
            string OwnerEmail,
            string Name,
            string Description,
            ClubType ClubType,
            string ClubImage,
            string Email,
            string Phone,
            string WebsiteUrl,
            string Location,
            int MaxMemberCount
        );
    }
}
