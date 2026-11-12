using System.Security.Cryptography;
using System.Text;
using System.Net.Http.Json;
using Fundoo.User.Application;
using Fundoo.User.Domain;
using Fundoo.User.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Http;

namespace Fundoo.User.Api;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly IHttpClientFactory _httpClientFactory;

    public AuthController(
        IAuthService auth,
        IUserRepository users,
        IPasswordHasher hasher,
        IHttpClientFactory httpClientFactory)
    {
        _auth = auth;
        _users = users;
        _hasher = hasher;
        _httpClientFactory = httpClientFactory;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        try
        {
            var id = await _auth.RegisterAsync(
                request.FirstName,
                request.LastName,
                request.Email,
                request.Password);

            return Created("", new RegisterResponse(id));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var token = await _auth.LoginAsync(request.Email, request.Password);

        if (token == null)
            return Unauthorized(new { message = "Invalid email or password." });

        return Ok(new LoginResponse(token));
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request)
    {
        var normalizedEmail = request.Email.Trim().ToUpperInvariant();
        var user = await _users.GetByEmailAsync(normalizedEmail);

        if (user != null)
        {
            var rawToken = Convert.ToBase64String(
                RandomNumberGenerator.GetBytes(32));

            var tokenHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

            await _users.AddResetTokenAsync(new PasswordResetToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = tokenHash,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15)
            });

            await _users.SaveAsync();

            var emailMessage = new
            {
                Email = user.Email,
                Subject = "Request for Password Reset",
                Body = $"Your password reset token is: {rawToken}"
            };

            var emailClient =
                _httpClientFactory.CreateClient("EmailService");

            var response = await emailClient.PostAsJsonAsync(
                "api/email/send",
                emailMessage);

            response.EnsureSuccessStatusCode();
        }

        return Accepted(new
        {
            message = "If the account exists, reset instructions will be sent."
        });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
    {
        var tokenHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(request.Token)));

        var reset = await _users.GetResetTokenAsync(tokenHash);

        if (reset == null)
            return BadRequest(new { message = "Invalid or expired reset token." });

        var user = await _users.GetByIdAsync(reset.UserId);

        if (user == null)
            return BadRequest(new { message = "Invalid or expired reset token." });

        user.PasswordHash = _hasher.Hash(request.Password);
        user.UpdatedAtUtc = DateTime.UtcNow;
        reset.UsedAtUtc = DateTime.UtcNow;

        await _users.SaveAsync();

        return Ok(new { message = "Password has been reset." });
    }
}