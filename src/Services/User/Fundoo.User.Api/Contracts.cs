namespace Fundoo.User.Api;

public record RegisterRequest(string FirstName, string LastName, string Email, string Password);
public record LoginRequest(string Email, string Password);
public record RegisterResponse(Guid UserId);
public record LoginResponse(string AccessToken);
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Token, string Password);
