using System.Security.Cryptography;
using System.Text;
using Fundoo.User.Domain;

namespace Fundoo.User.Application;

public class AuthService : IAuthService
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokens;

    public AuthService(IUserRepository users, IPasswordHasher hasher, ITokenService tokens)
    {
        _users = users;
        _hasher = hasher;
        _tokens = tokens;
    }

    public async Task<Guid> RegisterAsync(string firstName, string lastName, string email, string password)
    {
        var normalizedEmail = email.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName) ||
            string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("All fields are required.");

        if (await _users.GetByEmailAsync(normalizedEmail) != null)
            throw new InvalidOperationException("An account with this email already exists.");

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            Email = email.Trim(),
            NormalizedEmail = normalizedEmail,
            PasswordHash = _hasher.Hash(password),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        await _users.AddAsync(user);
        await _users.SaveAsync();
        return user.Id;
    }

    public async Task<string?> LoginAsync(string email, string password)
    {
        var user = await _users.GetByEmailAsync(email.Trim().ToUpperInvariant());
        if (user == null || !user.IsActive || !_hasher.Verify(password, user.PasswordHash))
            return null;

        return _tokens.CreateAccessToken(user);
    }
}
