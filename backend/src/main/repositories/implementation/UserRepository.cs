using backend.main.infrastructure.database.core;
using backend.main.application.security;
using backend.main.models.core;
using backend.main.repositories.contracts.users;
using backend.main.repositories.interfaces;

using Microsoft.EntityFrameworkCore;

namespace backend.main.repositories.implementation
{
    public class UserRepository : IUserRepository
    {
        private readonly AppDatabaseContext _context;

        public UserRepository(AppDatabaseContext context) => _context = context;

        public async Task<User> CreateUserAsync(User user)
        {
            user.Usertype = AuthRoles.NormalizeStored(user.Usertype);
            await _context.Users.AddAsync(user);
            await _context.SaveChangesAsync();
            return user;
        }

        public async Task<User?> UpdateUserAsync(int id, User updated)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
                return null;

            user.Password = updated.Password ?? user.Password;
            user.Usertype = updated.Usertype != null
                ? AuthRoles.NormalizeStored(updated.Usertype)
                : user.Usertype;
            user.Name = updated.Name ?? user.Name;
            user.Username = updated.Username ?? user.Username;
            user.Avatar = updated.Avatar ?? user.Avatar;
            user.Address = updated.Address ?? user.Address;
            user.Phone = updated.Phone ?? user.Phone;

            await _context.SaveChangesAsync();
            return user;
        }

        public async Task<User?> UpdatePartialAsync(User updated)
        {
            var existing = await _context.Users.FindAsync(updated.Id);
            if (existing == null)
                return null;

            if (updated.Email != null)
                existing.Email = updated.Email;
            if (updated.Password != null)
                existing.Password = updated.Password;
            if (updated.Usertype != null)
                existing.Usertype = AuthRoles.NormalizeStored(updated.Usertype);
            if (updated.Name != null)
                existing.Name = updated.Name;
            if (updated.Username != null)
                existing.Username = updated.Username;
            if (updated.Avatar != null)
                existing.Avatar = updated.Avatar;
            if (updated.Address != null)
                existing.Address = updated.Address;
            if (updated.Phone != null)
                existing.Phone = updated.Phone;

            await _context.SaveChangesAsync();
            return existing;
        }

        public async Task<UserOAuthRecord?> UpdateProviderIdsAsync(int id, string? googleId, string? microsoftId)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
                return null;

            if (googleId != null)
                user.GoogleID = googleId;
            if (microsoftId != null)
                user.MicrosoftID = microsoftId;

            await _context.SaveChangesAsync();
            return ToOAuthRecord(user);
        }

        public async Task<UserStatusRecord?> UpdateUserStatusAsync(int id, bool isDisabled, string? disabledReason)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
                return null;

            user.IsDisabled = isDisabled;
            user.DisabledAtUtc = isDisabled ? DateTime.UtcNow : null;
            user.DisabledReason = isDisabled ? disabledReason : null;
            user.AuthVersion += 1;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return new UserStatusRecord
            {
                Id = user.Id,
                IsDisabled = user.IsDisabled,
                DisabledAtUtc = user.DisabledAtUtc,
                DisabledReason = user.DisabledReason,
                AuthVersion = user.AuthVersion,
            };
        }

        public async Task<bool> IncrementAuthVersionAsync(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
                return false;

            user.AuthVersion += 1;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteUserAsync(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
                return false;

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<User?> GetUserAsync(int id)
        {
            return await _context.Users
                .AsNoTracking()
                .Where(u => u.Id == id)
                .Select(u => new User
                {
                    Id = u.Id,
                    Email = u.Email,
                    Password = null,
                    Usertype = AuthRoles.NormalizeStored(u.Usertype),
                    Name = u.Name,
                    Username = u.Username,
                    Avatar = u.Avatar,
                    Address = u.Address,
                    Phone = u.Phone,
                    MicrosoftID = u.MicrosoftID,
                    GoogleID = u.GoogleID,
                    IsDisabled = u.IsDisabled,
                    DisabledAtUtc = u.DisabledAtUtc,
                    DisabledReason = u.DisabledReason,
                    AuthVersion = u.AuthVersion,
                    CreatedAt = u.CreatedAt,
                    UpdatedAt = u.UpdatedAt,
                })
                .FirstOrDefaultAsync();
        }

        public async Task<UserAuthRecord?> GetAuthByEmailAsync(string email)
        {
            return await _context.Users
                .AsNoTracking()
                .Where(u => u.Email == email)
                .Select(u => new UserAuthRecord
                {
                    Id = u.Id,
                    Email = u.Email,
                    Password = u.Password,
                    Usertype = AuthRoles.NormalizeStored(u.Usertype),
                    IsDisabled = u.IsDisabled,
                    AuthVersion = u.AuthVersion,
                })
                .FirstOrDefaultAsync();
        }

        public async Task<UserOAuthRecord?> GetOAuthByEmailAsync(string email)
        {
            return await _context.Users
                .AsNoTracking()
                .Where(u => u.Email == email)
                .Select(u => new UserOAuthRecord
                {
                    Id = u.Id,
                    Email = u.Email,
                    Usertype = AuthRoles.NormalizeStored(u.Usertype),
                    GoogleID = u.GoogleID,
                    MicrosoftID = u.MicrosoftID,
                    IsDisabled = u.IsDisabled,
                    AuthVersion = u.AuthVersion,
                })
                .FirstOrDefaultAsync();
        }

        public async Task<UserOAuthRecord?> GetOAuthByMicrosoftIdAsync(string microsoftId)
        {
            return await _context.Users
                .AsNoTracking()
                .Where(u => u.MicrosoftID == microsoftId)
                .Select(u => new UserOAuthRecord
                {
                    Id = u.Id,
                    Email = u.Email,
                    Usertype = AuthRoles.NormalizeStored(u.Usertype),
                    GoogleID = u.GoogleID,
                    MicrosoftID = u.MicrosoftID,
                    IsDisabled = u.IsDisabled,
                    AuthVersion = u.AuthVersion,
                })
                .FirstOrDefaultAsync();
        }

        public async Task<UserOAuthRecord?> GetOAuthByGoogleIdAsync(string googleId)
        {
            return await _context.Users
                .AsNoTracking()
                .Where(u => u.GoogleID == googleId)
                .Select(u => new UserOAuthRecord
                {
                    Id = u.Id,
                    Email = u.Email,
                    Usertype = AuthRoles.NormalizeStored(u.Usertype),
                    GoogleID = u.GoogleID,
                    MicrosoftID = u.MicrosoftID,
                    IsDisabled = u.IsDisabled,
                    AuthVersion = u.AuthVersion,
                })
                .FirstOrDefaultAsync();
        }

        public async Task<UserProfileRecord?> GetProfileByUsernameAsync(string username)
        {
            return await _context.Users
                .AsNoTracking()
                .Where(u => u.Username == username)
                .Select(u => new UserProfileRecord
                {
                    Id = u.Id,
                    Email = u.Email,
                    Username = string.IsNullOrWhiteSpace(u.Username) ? u.Email : u.Username,
                    Name = u.Name,
                    Avatar = u.Avatar,
                    Usertype = AuthRoles.NormalizeStored(u.Usertype),
                })
                .FirstOrDefaultAsync();
        }

        public async Task<IReadOnlyList<UserListRecord>> GetUsersAsync(
            string? role = null,
            UserReadDetailLevel detail = UserReadDetailLevel.Slim
        )
        {
            var query = _context.Users
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrEmpty(role))
            {
                var normalizedRole = AuthRoles.NormalizeStored(role);
                query = query.Where(u => u.Usertype == normalizedRole);
            }

            return await ProjectUserListQuery(query, detail).ToListAsync();
        }

        public async Task<bool> EmailExistsAsync(string email)
        {
            return await _context.Users
                .AsNoTracking()
                .AnyAsync(u => u.Email == email);
        }

        public async Task<IReadOnlyList<UserListRecord>> GetByIdsAsync(
            IEnumerable<int> ids,
            UserReadDetailLevel detail = UserReadDetailLevel.Slim
        )
        {
            var idList = ids.Distinct().ToList();

            if (idList.Count == 0)
                return [];

            var users = await ProjectUserListQuery(
                    _context.Users
                        .AsNoTracking()
                        .Where(u => idList.Contains(u.Id)),
                    detail
                )
                .ToListAsync();

            return idList
                .Select(id => users.FirstOrDefault(u => u.Id == id))
                .Where(user => user != null)
                .Cast<UserListRecord>()
                .ToList();
        }

        private static IQueryable<UserListRecord> ProjectUserListQuery(
            IQueryable<User> query,
            UserReadDetailLevel detail
        )
        {
            if (detail == UserReadDetailLevel.Admin)
            {
                return query.Select(u => new UserListRecord
                {
                    Id = u.Id,
                    Email = u.Email,
                    Username = string.IsNullOrWhiteSpace(u.Username) ? u.Email : u.Username,
                    Name = u.Name,
                    Avatar = u.Avatar,
                    Usertype = AuthRoles.NormalizeStored(u.Usertype),
                    IsDisabled = u.IsDisabled,
                    DisabledAtUtc = u.DisabledAtUtc,
                    DisabledReason = u.DisabledReason,
                    CreatedAt = u.CreatedAt,
                    UpdatedAt = u.UpdatedAt,
                });
            }

            return query.Select(u => new UserListRecord
            {
                Id = u.Id,
                Email = u.Email,
                Username = string.IsNullOrWhiteSpace(u.Username) ? u.Email : u.Username,
                Name = u.Name,
                Avatar = u.Avatar,
                Usertype = AuthRoles.NormalizeStored(u.Usertype),
                IsDisabled = null,
                DisabledAtUtc = null,
                DisabledReason = null,
                CreatedAt = null,
                UpdatedAt = null,
            });
        }

        private static UserOAuthRecord ToOAuthRecord(User user)
        {
            return new UserOAuthRecord
            {
                Id = user.Id,
                Email = user.Email,
                Usertype = AuthRoles.NormalizeStored(user.Usertype),
                GoogleID = user.GoogleID,
                MicrosoftID = user.MicrosoftID,
                IsDisabled = user.IsDisabled,
                AuthVersion = user.AuthVersion,
            };
        }
    }
}
