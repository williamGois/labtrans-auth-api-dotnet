using System.Security.Cryptography;
using AuthApi.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AuthApi.Tests;

public sealed class AuthApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"auth-tests-{Guid.NewGuid()}";
    private readonly string _jwtSecret = GenerateTestJwtSecret();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Environment.SetEnvironmentVariable("JWT_SECRET", _jwtSecret);
        Environment.SetEnvironmentVariable("JWT_ISSUER", "labtrans-auth-api");
        Environment.SetEnvironmentVariable("JWT_AUDIENCE", "labtrans-reservas");
        Environment.SetEnvironmentVariable("JWT_EXPIRES_MINUTES", "60");
        Environment.SetEnvironmentVariable("AUTH_DB_CONNECTION_STRING", "InMemory");

        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JWT_SECRET"] = _jwtSecret,
                ["JWT_ISSUER"] = "labtrans-auth-api",
                ["JWT_AUDIENCE"] = "labtrans-reservas",
                ["JWT_EXPIRES_MINUTES"] = "60",
                ["AUTH_DB_CONNECTION_STRING"] = "InMemory"
            });
        });

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(service => service.ServiceType == typeof(DbContextOptions<AuthDbContext>));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<AuthDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));
        });
    }

    private static string GenerateTestJwtSecret()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    }
}
