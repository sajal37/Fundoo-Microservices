using Fundoo.User.Domain;
using Microsoft.EntityFrameworkCore;

namespace Fundoo.User.Infrastructure;

public class UserDbContext : DbContext
{
    public UserDbContext(DbContextOptions<UserDbContext> options)
        : base(options)
    {
    }

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<PasswordResetToken> PasswordResetTokens =>
        Set<PasswordResetToken>();

    public DbSet<LoginOtp> LoginOtps => Set<LoginOtp>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<AppUser>()
            .HasIndex(x => x.NormalizedEmail)
            .IsUnique();

        builder.Entity<AppUser>()
            .Property(x => x.Email)
            .HasMaxLength(256);

        builder.Entity<AppUser>()
            .Property(x => x.NormalizedEmail)
            .HasMaxLength(256);

        builder.Entity<PasswordResetToken>()
            .HasIndex(x => x.TokenHash)
            .IsUnique();

        builder.Entity<AppUser>()
            .HasIndex(x => x.GoogleSubject)
            .IsUnique();

        builder.Entity<LoginOtp>()
            .HasIndex(x => new
            {
                x.UserId,
                x.CreatedAtUtc
            });

        builder.Entity<LoginOtp>()
            .HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}