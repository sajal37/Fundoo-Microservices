using System.Security.Cryptography;
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
        _db.Users.FirstOrDefaultAsync(
            x => x.NormalizedEmail == normalizedEmail);

    public Task<AppUser?> GetByIdAsync(Guid id) =>
        _db.Users.FirstOrDefaultAsync(x => x.Id == id);

    public async Task AddAsync(AppUser user) =>
        await _db.Users.AddAsync(user);

    public Task SaveAsync() => _db.SaveChangesAsync();

    public async Task AddResetTokenAsync(PasswordResetToken token) =>
        await _db.PasswordResetTokens.AddAsync(token);

    public Task<PasswordResetToken?> GetResetTokenAsync(string tokenHash) =>
        _db.PasswordResetTokens.FirstOrDefaultAsync(x =>
            x.TokenHash == tokenHash &&
            x.UsedAtUtc == null &&
            x.ExpiresAtUtc > DateTime.UtcNow);

    public Task<AppUser?> GetByGoogleSubjectAsync(string googleSubject) =>
        _db.Users.FirstOrDefaultAsync(
            x => x.GoogleSubject == googleSubject);

    public Task<DateTime?> GetLatestLoginOtpCreatedAtAsync(Guid userId) =>
        _db.LoginOtps
            .Where(x =>
                x.UserId == userId &&
                x.UsedAtUtc == null &&
                x.ExpiresAtUtc > DateTime.UtcNow)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => (DateTime?)x.CreatedAtUtc)
            .FirstOrDefaultAsync();

    public async Task InvalidateLoginOtpsAsync(Guid userId)
    {
        var now = DateTime.UtcNow;

        await _db.LoginOtps
            .Where(x =>
                x.UserId == userId &&
                x.UsedAtUtc == null)
            .ExecuteUpdateAsync(update =>
                update.SetProperty(
                    x => x.UsedAtUtc,
                    (DateTime?)now));
    }

    public async Task AddLoginOtpAsync(LoginOtp otp) =>
        await _db.LoginOtps.AddAsync(otp);

    public async Task<Guid?> TryConsumeLoginOtpAsync(
        Guid challengeId,
        string codeHash)
    {
        var now = DateTime.UtcNow;

        var otp = await _db.LoginOtps
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Id == challengeId &&
                x.UsedAtUtc == null &&
                x.ExpiresAtUtc > now &&
                x.FailedAttempts < 5);

        if (otp == null)
        {
            return null;
        }

        var expectedHash = Convert.FromHexString(otp.CodeHash);
        var suppliedHash = Convert.FromHexString(codeHash);

        var matches = CryptographicOperations.FixedTimeEquals(
            expectedHash,
            suppliedHash);

        if (!matches)
        {
            await _db.LoginOtps
                .Where(x =>
                    x.Id == challengeId &&
                    x.UsedAtUtc == null &&
                    x.ExpiresAtUtc > now &&
                    x.FailedAttempts < 5)
                .ExecuteUpdateAsync(update =>
                    update.SetProperty(
                        x => x.FailedAttempts,
                        x => x.FailedAttempts + 1));

            return null;
        }

        var consumed = await _db.LoginOtps
            .Where(x =>
                x.Id == challengeId &&
                x.CodeHash == codeHash &&
                x.UsedAtUtc == null &&
                x.ExpiresAtUtc > now &&
                x.FailedAttempts < 5)
            .ExecuteUpdateAsync(update =>
                update.SetProperty(
                    x => x.UsedAtUtc,
                    (DateTime?)now));

        if (consumed != 1)
        {
            return null;
        }

        return otp.UserId;
    }
}