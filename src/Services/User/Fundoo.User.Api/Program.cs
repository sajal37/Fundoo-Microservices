
using FluentValidation;
using FluentValidation.AspNetCore;
using Fundoo.User.Api;
using Fundoo.User.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<KafkaProducerService>();

builder.Services.AddUserInfrastructure(builder.Configuration);

builder.Services.AddHttpClient("EmailService", client =>
{
    var baseUrl = builder.Configuration["EmailService:BaseUrl"];

    if (string.IsNullOrWhiteSpace(baseUrl))
    {
        throw new InvalidOperationException(
            "EmailService:BaseUrl is not configured.");
    }

    client.BaseAddress = new Uri(baseUrl);
});

var jwt = builder.Configuration.GetSection("Jwt");

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme =
            JwtBearerDefaults.AuthenticationScheme;

        options.DefaultChallengeScheme =
            JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwt["Audience"],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwt["SigningKey"]!))
        };
    })
    .AddCookie("External", options =>
    {
        options.Cookie.Name = "Fundoo.External";
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
        options.SlidingExpiration = false;
    })
    .AddOpenIdConnect("Google", options =>
    {
        options.SignInScheme = "External";

        options.Authority = "https://accounts.google.com";

        options.ClientId =
            builder.Configuration["Google:ClientId"]!;

        options.ClientSecret =
            builder.Configuration["Google:ClientSecret"]!;

        options.ResponseType = "code";
        options.ResponseMode = "query";
        options.CallbackPath = "/api/auth/signin-google";
        options.UsePkce = true;

        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.NonceCookie.SameSite = SameSiteMode.Lax;
        options.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

        options.MapInboundClaims = false;
        options.GetClaimsFromUserInfoEndpoint = true;

        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("email");
        options.Scope.Add("profile");

        options.ClaimActions.MapUniqueJsonKey(
    "email_verified", "email_verified");

        options.Events.OnRedirectToIdentityProvider = context =>
        {
            context.ProtocolMessage.ResponseMode = "query";

            var callbackUrl =
                builder.Configuration["Google:CallbackUrl"];

            if (!string.IsNullOrWhiteSpace(callbackUrl))
            {
                context.ProtocolMessage.RedirectUri = callbackUrl;
            }

            return Task.CompletedTask;
        };

        options.Events.OnRemoteFailure = async context =>
        {
            if (string.IsNullOrEmpty(context.Request.Query["state"]))
            {
                context.Response.Redirect("/api/auth/google");
                context.HandleResponse();
                return;
            }

            context.Response.StatusCode = 400;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Authentication failed",
                details = context.Failure?.Message
            });
            context.HandleResponse();
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "user"
}));

app.MapControllers();

app.Run();
