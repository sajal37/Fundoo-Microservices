using System.Security.Cryptography;
using System.Text;
using System.Net.Http.Json;
using Fundoo.User.Application;
using Fundoo.User.Domain;
using Fundoo.User.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Http;
using Microsoft.AspNetCore.Authentication;

namespace Fundoo.User.Api;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITokenService _tokens;

    public AuthController(
        IAuthService auth,
        IUserRepository users,
        IPasswordHasher hasher,
        IHttpClientFactory httpClientFactory,
        ITokenService tokens)
    {
        _auth = auth;
        _users = users;
        _hasher = hasher;
        _httpClientFactory = httpClientFactory;
        _tokens = tokens;
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

    [HttpGet("google")]
    public IActionResult GoogleLogin()
    {
        var properties = new AuthenticationProperties
        {
            RedirectUri = "/api/auth/google-callback"
        };

        return Challenge(properties, "Google");
    }

    [HttpGet("google-callback")]
    public async Task<IActionResult> GoogleCallback()
    {
        var result = await HttpContext.AuthenticateAsync("External");

        if (!result.Succeeded || result.Principal == null)
        {
            await HttpContext.SignOutAsync("External");

            return Unauthorized(new
            {
                message = "Google authentication failed."
            });
        }

        var principal = result.Principal;

        var googleSubject = principal.FindFirst("sub")?.Value;
        var email = principal.FindFirst("email")?.Value;
        var emailVerified = principal.FindFirst("email_verified")?.Value;

        if (string.IsNullOrWhiteSpace(googleSubject) ||
            string.IsNullOrWhiteSpace(email) ||
            !string.Equals(
                emailVerified,
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            await HttpContext.SignOutAsync("External");

            return Unauthorized(new
            {
                message = "Google email is missing or not verified."
            });
        }

        var normalizedEmail = email.Trim().ToUpperInvariant();

        var user = await _users.GetByGoogleSubjectAsync(googleSubject);

        if (user == null)
        {
            user = await _users.GetByEmailAsync(normalizedEmail);

            if (user == null)
            {
                user = new AppUser
                {
                    Id = Guid.NewGuid(),
                    FirstName = principal.FindFirst("given_name")?.Value ?? "",
                    LastName = principal.FindFirst("family_name")?.Value ?? "",
                    Email = email.Trim(),
                    NormalizedEmail = normalizedEmail,
                    GoogleSubject = googleSubject,
                    PasswordHash = _hasher.Hash(
                        Convert.ToBase64String(
                            RandomNumberGenerator.GetBytes(32))),
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                };

                await _users.AddAsync(user);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(user.GoogleSubject) &&
                    user.GoogleSubject != googleSubject)
                {
                    await HttpContext.SignOutAsync("External");

                    return Conflict(new
                    {
                        message = "This account is linked to another Google account."
                    });
                }

                user.GoogleSubject = googleSubject;
                user.UpdatedAtUtc = DateTime.UtcNow;
            }

            await _users.SaveAsync();
        }

        await HttpContext.SignOutAsync("External");

        if (!user.IsActive)
        {
            return Forbid();
        }

        var token = _tokens.CreateAccessToken(user);

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