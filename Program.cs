using System.Text;
using AuthApi.Configuration;
using AuthApi.Data;
using AuthApi.Observability;
using AuthApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
});
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);

var jwtSettings = JwtSettings.FromConfiguration(builder.Configuration);
builder.Services.Configure<JwtSettings>(options =>
{
    options.Secret = jwtSettings.Secret;
    options.Issuer = jwtSettings.Issuer;
    options.Audience = jwtSettings.Audience;
    options.ExpiresMinutes = jwtSettings.ExpiresMinutes;
});

var connectionString = builder.Configuration["AUTH_DB_CONNECTION_STRING"]
    ?? builder.Configuration.GetConnectionString("AuthDb");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("AUTH_DB_CONNECTION_STRING must be configured.");
}

builder.Services.AddDbContext<AuthDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped<IAuthService, AuthService>();

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var problem = ApiErrorFactory.Create(
                context.HttpContext,
                StatusCodes.Status400BadRequest,
                "Validation error",
                "Dados invalidos.");
            return new BadRequestObjectResult(problem);
        };
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Labtrans Reservas - Auth API",
        Version = "v1",
        Description = "Microsservico ASP.NET Core responsavel por cadastro, login e emissao de JWT."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Informe o token JWT como: Bearer {token}",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var corsOrigins = builder.Configuration["CORS_ORIGINS"]
    ?? builder.Configuration["FRONTEND_ORIGIN"]
    ?? "http://localhost:5173,http://127.0.0.1:5173";

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins(corsOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .AllowAnyHeader()
            .AllowAnyMethod());
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                AuthMetrics.ProtectedRouteUnauthorized.WithLabels("challenge").Inc();

                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("JwtBearer");
                logger.LogWarning(
                    "Unauthorized protected route request rejected. correlationId={CorrelationId}",
                    CorrelationContext.Get(context.HttpContext));

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/problem+json";
                var problem = ApiErrorFactory.Create(
                    context.HttpContext,
                    StatusCodes.Status401Unauthorized,
                    "Unauthorized",
                    "Token ausente ou invalido.");
                await context.Response.WriteAsJsonAsync(problem);
            },
            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("JwtBearer");
                logger.LogWarning(
                    "JWT validation failed. correlationId={CorrelationId}",
                    CorrelationContext.Get(context.HttpContext));
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

if (string.Equals(builder.Configuration["OTEL_TRACES_EXPORTER"], "otlp", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService(
            builder.Configuration["OTEL_SERVICE_NAME"] ?? AuthMetrics.ServiceName))
        .WithTracing(tracing =>
        {
            tracing
                .AddAspNetCoreInstrumentation()
                .AddOtlpExporter();
        });
}

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("APPLY_MIGRATIONS"))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    dbContext.Database.Migrate();
}

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("Frontend");
app.UseCorrelationId();
app.UseRequestTelemetry();
app.UseStandardExceptionHandling();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health", (HttpContext context) => Results.Ok(new
    {
        status = "ok",
        service = AuthMetrics.ServiceName,
        timestamp = DateTime.UtcNow,
        correlationId = CorrelationContext.Get(context)
    }))
    .AllowAnonymous()
    .WithTags("health");
app.MapGet("/health/live", (HttpContext context) => Results.Ok(new
    {
        status = "ok",
        service = AuthMetrics.ServiceName,
        timestamp = DateTime.UtcNow,
        correlationId = CorrelationContext.Get(context)
    }))
    .AllowAnonymous()
    .WithTags("health");
app.MapGet("/health/ready", async (AuthDbContext dbContext, HttpContext context, IOptions<JwtSettings> options) =>
    {
        var checks = new Dictionary<string, string>();
        var configurationOk =
            !string.IsNullOrWhiteSpace(options.Value.Secret)
            && options.Value.Secret.Length >= 32
            && !string.IsNullOrWhiteSpace(options.Value.Issuer)
            && !string.IsNullOrWhiteSpace(options.Value.Audience);
        checks["configuration"] = configurationOk ? "ok" : "error";

        try
        {
            var providerName = dbContext.Database.ProviderName ?? string.Empty;
            var databaseOk = providerName.Contains("InMemory", StringComparison.OrdinalIgnoreCase)
                || await dbContext.Database.CanConnectAsync();
            checks["database"] = databaseOk ? "ok" : "error";
        }
        catch
        {
            checks["database"] = "error";
        }

        var ready = checks.Values.All(value => value == "ok");
        var response = new
        {
            status = ready ? "ready" : "not_ready",
            service = AuthMetrics.ServiceName,
            checks,
            timestamp = DateTime.UtcNow,
            correlationId = CorrelationContext.Get(context)
        };

        return Results.Json(response, statusCode: ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
    })
    .AllowAnonymous()
    .WithTags("health");
app.MapMetrics("/metrics");
app.MapControllers();
app.Run();

public partial class Program
{
}
