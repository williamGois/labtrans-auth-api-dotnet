using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AuthApi.Configuration;
using AuthApi.Data;
using AuthApi.Dtos;
using AuthApi.Entities;
using AuthApi.Exceptions;
using AuthApi.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AuthApi.Services;

public sealed class AuthService(
    AuthDbContext dbContext,
    IOptions<JwtSettings> jwtOptions,
    ILogger<AuthService> logger) : IAuthService
{
    private readonly JwtSettings _jwtSettings = jwtOptions.Value;

    public async Task<UserResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var exists = await dbContext.Users.AnyAsync(user => user.Email == normalizedEmail, cancellationToken);
        if (exists)
        {
            AuthMetrics.RegisterFailure.WithLabels("duplicate_email").Inc();
            logger.LogWarning("Registration rejected because email already exists.");
            throw new DuplicateEmailException("E-mail ja cadastrado.");
        }

        var user = new User
        {
            Email = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password)
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);
        AuthMetrics.RegisterSuccess.Inc();
        logger.LogInformation("User registered successfully. userId={UserId}", user.Id);
        return ToUserResponse(user);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var user = await dbContext.Users.SingleOrDefaultAsync(candidate => candidate.Email == normalizedEmail, cancellationToken);
        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            AuthMetrics.LoginFailure.WithLabels("invalid_credentials").Inc();
            AuthMetrics.InvalidCredentials.Inc();
            logger.LogWarning("Login rejected by invalid credentials.");
            throw new InvalidCredentialsException("E-mail ou senha invalidos.");
        }

        var expiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpiresMinutes);
        var token = GenerateToken(user, expiresAt);
        AuthMetrics.LoginSuccess.Inc();
        AuthMetrics.JwtIssued.Inc();
        logger.LogInformation("Login succeeded and JWT was issued. userId={UserId}", user.Id);

        return new AuthResponse(
            token,
            "Bearer",
            _jwtSettings.ExpiresMinutes * 60,
            ToUserResponse(user));
    }

    public async Task<UserResponse?> GetUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.FindAsync([userId], cancellationToken);
        return user is null ? null : ToUserResponse(user);
    }

    private string GenerateToken(User user, DateTime expiresAt)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.Email, user.Email)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static UserResponse ToUserResponse(User user) => new(user.Id, user.Email, user.CreatedAt);
}
