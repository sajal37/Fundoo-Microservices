using Fundoo.User.Domain;

namespace Fundoo.User.Application;

public interface IUserRepository
{
    Task<AppUser?> GetByEmailAsync(string normalizedEmail);
    Task<AppUser?> GetByIdAsync(Guid id);
    Task AddAsync(AppUser user);
    Task SaveAsync();
    Task AddResetTokenAsync(PasswordResetToken token);
    Task<PasswordResetToken?> GetResetTokenAsync(string tokenHash);
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

public interface ITokenService
{
    string CreateAccessToken(AppUser user);
}

public interface IAuthService
{
    Task<Guid> RegisterAsync(string firstName, string lastName, string email, string password);
    Task<string?> LoginAsync(string email, string password);
}
