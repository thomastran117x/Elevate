using backend.main.infrastructure.database.core;
using backend.main.application.security;
using backend.main.models.core;

using Microsoft.EntityFrameworkCore;

namespace backend.main.seeders
{
    public sealed class AuthUsersSeeder : ISeeder
    {
        private const int UserCount = 20;
        private const string DefaultUserPassword = "Password123!";
        private const string DefaultAdminEmail = "admin@seed.eventxperience.test";
        private const string DefaultAdminUsername = "seedadmin";
        private const string DefaultAdminName = "Seed Admin";
        private readonly AppDatabaseContext _dbContext;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthUsersSeeder> _logger;

        public AuthUsersSeeder(
            AppDatabaseContext dbContext,
            IConfiguration configuration,
            ILogger<AuthUsersSeeder> logger
        )
        {
            _dbContext = dbContext;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task SeedAsync(CancellationToken cancellationToken = default)
        {
            var seededUsers = BuildSeedUsers();
            var seededEmails = seededUsers
                .Select(user => user.Email)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var existingEmails = await _dbContext.Users
                .AsNoTracking()
                .Where(user => seededEmails.Contains(user.Email))
                .Select(user => user.Email)
                .ToListAsync(cancellationToken);

            var existingLookup = existingEmails.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missingUsers = seededUsers
                .Where(user => !existingLookup.Contains(user.Email))
                .ToList();

            if (missingUsers.Count == 0)
            {
                _logger.LogInformation("[Seeders] Auth users already present. No new users added.");
                return;
            }

            await _dbContext.Users.AddRangeAsync(missingUsers, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "[Seeders] Seeded {Count} auth user records.",
                missingUsers.Count
            );
        }

        private List<User> BuildSeedUsers()
        {
            var normalizedRoles = new[]
            {
                AuthRoles.Participant,
                AuthRoles.Organizer,
                AuthRoles.Volunteer
            };
            var userPasswordHash = BCrypt.Net.BCrypt.HashPassword(
                ResolveDefaultUserPassword(),
                workFactor: 12
            );
            var adminPasswordHash = BCrypt.Net.BCrypt.HashPassword(
                ResolveAdminPassword(),
                workFactor: 12
            );
            var users = new List<User>(UserCount + 1)
            {
                BuildAdminUser(adminPasswordHash)
            };

            for (var i = 1; i <= UserCount; i++)
            {
                var role = normalizedRoles[(i - 1) % normalizedRoles.Length];
                var index = i.ToString("D2");

                users.Add(new User
                {
                    Email = $"local.user{index}@seed.eventxperience.test",
                    Password = userPasswordHash,
                    Usertype = role,
                    Name = $"Local User {index}",
                    Username = $"localuser{index}",
                    Avatar = null,
                    Address = null,
                    Phone = null,
                    GoogleID = null,
                    MicrosoftID = null,
                });
            }

            return users;
        }

        private User BuildAdminUser(string passwordHash)
        {
            return new User
            {
                Email = ResolveEnvFirstValue("AUTH_SEED_ADMIN_EMAIL", "Seeders:AuthUsers:AdminEmail")
                    ?? DefaultAdminEmail,
                Password = passwordHash,
                Usertype = AuthRoles.Admin,
                Name = ResolveEnvFirstValue("AUTH_SEED_ADMIN_NAME", "Seeders:AuthUsers:AdminName")
                    ?? DefaultAdminName,
                Username = ResolveEnvFirstValue(
                    "AUTH_SEED_ADMIN_USERNAME",
                    "Seeders:AuthUsers:AdminUsername"
                ) ?? DefaultAdminUsername,
                Avatar = null,
                Address = null,
                Phone = null,
                GoogleID = null,
                MicrosoftID = null,
            };
        }

        private string ResolveDefaultUserPassword()
        {
            return ResolveEnvFirstValue("AUTH_SEED_USERS_PASSWORD", "Seeders:AuthUsers:Password")
                ?? DefaultUserPassword;
        }

        private string ResolveAdminPassword()
        {
            return ResolveEnvFirstValue(
                "AUTH_SEED_ADMIN_PASSWORD",
                "Seeders:AuthUsers:AdminPassword"
            )
                ?? ResolveDefaultUserPassword();
        }

        private string? ResolveEnvFirstValue(string envKey, string configKey)
        {
            return _configuration[envKey] ?? _configuration[configKey];
        }
    }
}
