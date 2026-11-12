using Fundoo.User.Application;
using Fundoo.User.Domain;
using Microsoft.EntityFrameworkCore;

namespace Fundoo.User.Infrastructure;

public class UserRepository : IUserRepository
{
    private readonly UserDbContext _db;

    public UserRepository(UserDbContext db)
    {
        _db = db;
    }

    public Task<AppUser?> GetByEmailAsync(string normalizedEmail) =>
        _db.Users.FirstOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail);

    public Task<AppUser?> GetByIdAsync(Guid id) =>
        _db.Users.FirstOrDefaultAsync(x => x.Id == id);

    public async Task AddAsync(AppUser user) => await _db.Users.AddAsync(user);

    public Task SaveAsync() => _db.SaveChangesAsync();

    public async Task AddResetTokenAsync(PasswordResetToken token) => await _db.PasswordResetTokens.AddAsync(token);

    public Task<PasswordResetToken?> GetResetTokenAsync(string tokenHash) =>
        _db.PasswordResetTokens.FirstOrDefaultAsync(x => x.TokenHash == tokenHash && x.UsedAtUtc == null && x.ExpiresAtUtc > DateTime.UtcNow);
}
