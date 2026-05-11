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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Environment.SetEnvironmentVariable("JWT_SECRET", "TEST_JWT_SECRET_REDACTED");
        Environment.SetEnvironmentVariable("JWT_ISSUER", "labtrans-auth-api");
        Environment.SetEnvironmentVariable("JWT_AUDIENCE", "labtrans-reservas");
        Environment.SetEnvironmentVariable("JWT_EXPIRES_MINUTES", "60");

        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JWT_SECRET"] = "TEST_JWT_SECRET_REDACTED",
                ["JWT_ISSUER"] = "labtrans-auth-api",
                ["JWT_AUDIENCE"] = "labtrans-reservas",
                ["JWT_EXPIRES_MINUTES"] = "60"
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
}
