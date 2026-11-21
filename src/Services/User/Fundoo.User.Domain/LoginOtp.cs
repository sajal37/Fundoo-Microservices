namespace Fundoo.User.Domain;

public class LoginOtp
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string CodeHash { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public int FailedAttempts { get; set; }
}