using backend.main.Interfaces;
using backend.main.Models;
using backend.main.Resources;

using Microsoft.EntityFrameworkCore;

namespace backend.main.Repositories
{
    public class UserRepository : BaseRepository, IUserRepository
    {
        public UserRepository(AppDatabaseContext context)
            : base(context) { }

        public async Task<User> CreateUserAsync(User user)
        {
            return await ExecuteAsync(async () =>
            {
                await _context.Users.AddAsync(user);
                await _context.SaveChangesAsync();
                return user;
            });
        }

        public async Task<User?> UpdateUserAsync(int id, User updated)
        {
            return await ExecuteAsync(async () =>
            {
                var user = await _context.Users.FindAsync(id);
                if (user == null)
                    return null;

                user.Password = updated.Password ?? user.Password;
                user.Usertype = updated.Usertype ?? user.Usertype;
                user.Name = updated.Name ?? user.Name;
                user.Username = updated.Username ?? user.Username;
                user.Avatar = updated.Avatar ?? user.Avatar;
                user.Address = updated.Address ?? user.Address;
                user.Phone = updated.Phone ?? user.Phone;

                await _context.SaveChangesAsync();
                return user;
            });
        }

        public async Task<User?> UpdatePartialAsync(User updated)
        {
            return await ExecuteAsync(async () =>
            {
                var existing = await _context.Users.FindAsync(updated.Id);
                if (existing == null)
                    return null;

                if (updated.Email != null)
                    existing.Email = updated.Email;
                if (updated.Password != null)
                    existing.Password = updated.Password;
                if (updated.Usertype != null)
                    existing.Usertype = updated.Usertype;
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
            });
        }

        public async Task<User?> UpdateProviderIdsAsync(int id, string? googleId, string? microsoftId)
        {
            return await ExecuteAsync(async () =>
            {
                var user = await _context.Users.FindAsync(id);
                if (user == null)
                    return null;

                if (googleId != null)
                    user.GoogleID = googleId;
                if (microsoftId != null)
                    user.MicrosoftID = microsoftId;

                await _context.SaveChangesAsync();
                return user;
            });
        }

        public async Task<bool> DeleteUserAsync(int id)
        {
            return await ExecuteAsync(async () =>
            {
                var user = await _context.Users.FindAsync(id);
                if (user == null)
                    return false;

                _context.Users.Remove(user);
                await _context.SaveChangesAsync();
                return true;
            });
        }

        public async Task<User?> GetUserAsync(int id)
        {
            return await ExecuteAsync(async () =>
            {
                return await _context.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == id);
            });
        }

        public async Task<User?> GetUserByEmailAsync(string email)
        {
            return await ExecuteAsync(async () =>
            {
                return await _context.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Email == email);
            });
        }

        public async Task<User?> GetUserByMicrosoftIdAsync(string microsoftId)
        {
            return await ExecuteAsync(async () =>
            {
                return await _context.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.MicrosoftID == microsoftId);
            });
        }

        public async Task<User?> GetUserByGoogleIdAsync(string googleId)
        {
            return await ExecuteAsync(async () =>
            {
                return await _context.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.GoogleID == googleId);
            });
        }

        public async Task<User?> GetUserByUsernameAsync(string username)
        {
            return await ExecuteAsync(async () =>
            {
                return await _context.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Username == username);
            });
        }

        public async Task<IEnumerable<User>> GetUsersAsync(string? role = null)
        {
            return await ExecuteAsync(async () =>
            {
                var query = _context.Users
                    .AsNoTracking()
                    .AsQueryable();

                if (!string.IsNullOrEmpty(role))
                    query = query.Where(u => u.Usertype == role);

                return await query.ToListAsync();
            });
        }

        public async Task<bool> EmailExistsAsync(string email)
        {
            return await ExecuteAsync(async () =>
            {
                return await _context.Users
                    .AsNoTracking()
                    .AnyAsync(u => u.Email == email);
            });
        }

        public async Task<List<User>> GetByIdsAsync(IEnumerable<int> ids)
        {
            return await ExecuteAsync(async () =>
            {
                var idList = ids.Distinct().ToList();

                if (idList.Count == 0)
                    return new List<User>();

                return await _context.Users
                    .AsNoTracking()
                    .Where(u => idList.Contains(u.Id))
                    .ToListAsync();
            })!;
        }
    }
}
