using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Fundoo.User.Application;
using Fundoo.User.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

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
    private readonly IConfiguration _configuration;
    private readonly KafkaProducerService _kafkaProducer;

    public AuthController(
        IAuthService auth,
        IUserRepository users,
        IPasswordHasher hasher,
        IHttpClientFactory httpClientFactory,
        ITokenService tokens,
        IConfiguration configuration,
        KafkaProducerService kafkaProducer)
    {
        _auth = auth;
        _users = users;
        _hasher = hasher;
        _httpClientFactory = httpClientFactory;
        _tokens = tokens;
        _configuration = configuration;
        _kafkaProducer = kafkaProducer;
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
        var user = await _auth.AuthenticateAsync(
            request.Email,
            request.Password);

        if (user == null)
        {
            return Unauthorized(new
            {
                message = "Invalid email or password."
            });
        }

        return await SendTwoFactorCodeAsync(user);
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

        return await SendTwoFactorCodeAsync(user);
    }

    [HttpPost("verify-two-factor")]
    public async Task<IActionResult> VerifyTwoFactor(
        VerifyTwoFactorRequest request)
    {
        var codeHash = HashTwoFactorCode(request.Code);

        var userId = await _users.TryConsumeLoginOtpAsync(
            request.ChallengeId,
            codeHash);

        if (userId == null)
        {
            return Unauthorized(new
            {
                message = "Invalid or expired verification code."
            });
        }

        var user = await _users.GetByIdAsync(userId.Value);

        if (user == null || !user.IsActive)
        {
            return Unauthorized(new
            {
                message = "User account is unavailable."
            });
        }

        var token = _tokens.CreateAccessToken(user);

        return Ok(new LoginResponse(token));
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(
        ForgotPasswordRequest request)
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

            var subject = "Request for Password Reset";
            var body = $"Your password reset token is: {rawToken}";

            bool sentViaKafka = await _kafkaProducer.SendEmailAsync(user.Email, subject, body);

            if (!sentViaKafka)
            {
                var emailMessage = new
                {
                    Email = user.Email,
                    Subject = subject,
                    Body = body
                };

                var emailClient =
                    _httpClientFactory.CreateClient("EmailService");

                var response = await emailClient.PostAsJsonAsync(
                    "api/email/send",
                    emailMessage);

                response.EnsureSuccessStatusCode();
            }
        }

        return Accepted(new
        {
            message = "If the account exists, reset instructions will be sent."
        });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(
        ResetPasswordRequest request)
    {
        var tokenHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(request.Token)));

        var reset = await _users.GetResetTokenAsync(tokenHash);

        if (reset == null)
        {
            return BadRequest(new
            {
                message = "Invalid or expired reset token."
            });
        }

        var user = await _users.GetByIdAsync(reset.UserId);

        if (user == null)
        {
            return BadRequest(new
            {
                message = "Invalid or expired reset token."
            });
        }

        user.PasswordHash = _hasher.Hash(request.Password);
        user.UpdatedAtUtc = DateTime.UtcNow;
        reset.UsedAtUtc = DateTime.UtcNow;

        await _users.InvalidateLoginOtpsAsync(user.Id);
        await _users.SaveAsync();

        return Ok(new
        {
            message = "Password has been reset."
        });
    }

    private async Task<IActionResult> SendTwoFactorCodeAsync(AppUser user)
    {
        var now = DateTime.UtcNow;

        var latestCodeTime =
            await _users.GetLatestLoginOtpCreatedAtAsync(user.Id);

        if (latestCodeTime.HasValue &&
            now - latestCodeTime.Value < TimeSpan.FromSeconds(30))
        {
            return StatusCode(429, new
            {
                message = "A code was recently sent. Wait 30 seconds before trying again."
            });
        }

        await _users.InvalidateLoginOtpsAsync(user.Id);

        var code = RandomNumberGenerator
            .GetInt32(0, 1_000_000)
            .ToString("D6");

        var otp = new LoginOtp
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CodeHash = HashTwoFactorCode(code),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(5)
        };

        await _users.AddLoginOtpAsync(otp);
        await _users.SaveAsync();

        var subject = "Fundoo login verification code";
        var body = $"<p>Your Fundoo login code is <strong>{code}</strong>.</p>" +
                   "<p>This code expires in five minutes. Do not share it.</p>";

        bool sentViaKafka = await _kafkaProducer.SendEmailAsync(user.Email, subject, body);

        if (!sentViaKafka)
        {
            var emailClient =
                _httpClientFactory.CreateClient("EmailService");

            try
            {
                using var response = await emailClient.PostAsJsonAsync(
                    "api/email/send",
                    new
                    {
                        Email = user.Email,
                        Subject = subject,
                        Body = body
                    });

                if (!response.IsSuccessStatusCode)
                {
                    await _users.InvalidateLoginOtpsAsync(user.Id);

                    return StatusCode(502, new
                    {
                        message = "The verification email could not be sent."
                    });
                }
            }
            catch (HttpRequestException)
            {
                await _users.InvalidateLoginOtpsAsync(user.Id);

                return StatusCode(502, new
                {
                    message = "The verification email service is unavailable."
                });
            }
            catch (TaskCanceledException)
            {
                await _users.InvalidateLoginOtpsAsync(user.Id);

                return StatusCode(502, new
                {
                    message = "The verification email service timed out."
                });
            }
        }

        return Accepted(new
        {
            message = "A verification code was sent to your email address.",
            challengeId = otp.Id,
            expiresInMinutes = 5,
            resendAfterSeconds = 30
        });
    }

    private string HashTwoFactorCode(string code)
    {
        var signingKey = _configuration["Jwt:SigningKey"];

        if (string.IsNullOrWhiteSpace(signingKey))
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey is not configured.");
        }

        var keyBytes = Encoding.UTF8.GetBytes(signingKey);
        var codeBytes = Encoding.UTF8.GetBytes("Fundoo:LoginOtp:" + code);

        return Convert.ToHexString(
            HMACSHA256.HashData(keyBytes, codeBytes));
    }
}