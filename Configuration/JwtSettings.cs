using System.Text;

namespace AuthApi.Configuration;

public sealed class JwtSettings
{
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "labtrans-auth-api";
    public string Audience { get; set; } = "labtrans-reservas";
    public int ExpiresMinutes { get; set; } = 60;

    public static JwtSettings FromConfiguration(IConfiguration configuration)
    {
        var secret = configuration["JWT_SECRET"] ?? configuration["Jwt:Secret"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException("JWT_SECRET precisa ser configurado.");
        }

        if (Encoding.UTF8.GetByteCount(secret) < 32)
        {
            throw new InvalidOperationException("JWT_SECRET precisa ter ao menos 32 bytes.");
        }

        var expires = configuration["JWT_EXPIRES_MINUTES"] ?? configuration["Jwt:ExpiresMinutes"];
        return new JwtSettings
        {
            Secret = secret,
            Issuer = configuration["JWT_ISSUER"] ?? configuration["Jwt:Issuer"] ?? "labtrans-auth-api",
            Audience = configuration["JWT_AUDIENCE"] ?? configuration["Jwt:Audience"] ?? "labtrans-reservas",
            ExpiresMinutes = int.TryParse(expires, out var parsed) ? parsed : 60
        };
    }
}
