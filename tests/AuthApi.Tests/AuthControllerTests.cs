using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using AuthApi.Data;
using AuthApi.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AuthApi.Tests;

public sealed class AuthControllerTests : IClassFixture<AuthApiFactory>
{
    private readonly HttpClient _client;
    private readonly AuthApiFactory _factory;

    public AuthControllerTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await _client.GetAsync("/health");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task LiveHealth_ReturnsCorrelationId()
    {
        var response = await _client.GetAsync("/health/live");
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Correlation-ID", out var values));
        Assert.False(string.IsNullOrWhiteSpace(values.Single()));
        Assert.NotNull(body);
        Assert.True(body!.ContainsKey("correlationId"));
    }

    [Fact]
    public async Task ReadyHealth_ReturnsDatabaseAndConfigurationChecks()
    {
        var response = await _client.GetAsync("/health/ready");
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.True(body!.ContainsKey("checks"));
    }

    [Fact]
    public async Task Response_PreservesClientCorrelationId()
    {
        const string correlationId = "test-correlation-id-123";
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Correlation-ID", correlationId);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Correlation-ID", out var values));
        Assert.Equal(correlationId, values.Single());
    }

    [Fact]
    public async Task Metrics_ReturnsPrometheusMetrics()
    {
        var response = await _client.GetAsync("/metrics");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("http_requests_total", body);
        Assert.Contains("auth_login_success_total", body);
    }

    [Fact]
    public async Task Register_WithValidPayload_ReturnsCreatedUserWithoutPassword()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(UniqueEmail("user1"), "TEST_CREDENTIAL_REDACTED"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(user);
        Assert.NotEqual(Guid.Empty, user!.Id);
        Assert.EndsWith("@email.com", user.Email);
    }

    [Fact]
    public async Task Register_WithDuplicatedEmail_ReturnsConflict()
    {
        var request = new RegisterRequest("duplicado@email.com", "TEST_CREDENTIAL_REDACTED");
        await _client.PostAsJsonAsync("/api/auth/register", request);

        var response = await _client.PostAsJsonAsync("/api/auth/register", request);
        var problem = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Duplicate email", problem?.Title);
        Assert.False(string.IsNullOrWhiteSpace(problem?.CorrelationId));
    }

    [Theory]
    [InlineData("email-invalido", "TEST_CREDENTIAL_REDACTED")]
    [InlineData("email@email.com", "")]
    [InlineData("email@email.com", "12345")]
    public async Task Register_WithInvalidPayload_ReturnsBadRequest(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, password));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsJwt()
    {
        var request = new RegisterRequest("login@email.com", "TEST_CREDENTIAL_REDACTED");
        await _client.PostAsJsonAsync("/api/auth/register", request);

        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(request.Email, request.Password));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        Assert.Equal("Bearer", auth!.TokenType);
        Assert.False(string.IsNullOrWhiteSpace(auth.AccessToken));
        Assert.Equal(3600, auth.ExpiresIn);
        Assert.Equal("login@email.com", auth.User.Email);
    }

    [Fact]
    public async Task Login_WithInvalidPassword_ReturnsUnauthorized()
    {
        await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("invalid@email.com", "TEST_CREDENTIAL_REDACTED"));

        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest("invalid@email.com", "errada"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithUnknownUser_ReturnsUnauthorizedWithoutLeakingWhichFieldFailed()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(UniqueEmail("unknown"), "TEST_CREDENTIAL_REDACTED"));
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("E-mail ou senha invalidos.", error?.Detail);
        Assert.False(string.IsNullOrWhiteSpace(error?.CorrelationId));
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsJwtWithRequiredClaims()
    {
        var email = UniqueEmail("claims");
        var password = "TEST_CREDENTIAL_REDACTED";
        await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, password));

        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(auth);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(auth!.AccessToken);
        Assert.Equal("labtrans-auth-api", token.Issuer);
        Assert.Contains("labtrans-reservas", token.Audiences);
        Assert.True(token.ValidTo > DateTime.UtcNow);
        Assert.Contains(token.Claims, claim => claim.Type == JwtRegisteredClaimNames.Sub && Guid.TryParse(claim.Value, out _));
        Assert.Contains(token.Claims, claim => claim.Type == JwtRegisteredClaimNames.Email && claim.Value == email);
    }

    [Fact]
    public async Task Register_DoesNotPersistPlainTextPassword()
    {
        var email = UniqueEmail("hash");
        var password = "TEST_CREDENTIAL_REDACTED";
        await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, password));

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var user = await dbContext.Users.SingleAsync(candidate => candidate.Email == email);

        Assert.NotEqual(password, user.PasswordHash);
        Assert.StartsWith("$2", user.PasswordHash);
        Assert.True(BCrypt.Net.BCrypt.Verify(password, user.PasswordHash));
    }

    [Fact]
    public async Task Me_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/auth/me");
        var problem = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Unauthorized", problem?.Title);
        Assert.False(string.IsNullOrWhiteSpace(problem?.CorrelationId));
    }

    [Fact]
    public async Task Me_WithValidToken_ReturnsAuthenticatedUser()
    {
        var email = UniqueEmail("me");
        var password = "TEST_CREDENTIAL_REDACTED";
        await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, password));
        var authResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        var auth = await authResponse.Content.ReadFromJsonAsync<AuthResponse>();

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        var response = await _client.GetAsync("/api/auth/me");
        var me = await response.Content.ReadFromJsonAsync<UserResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(email, me?.Email);
        Assert.Equal(auth.User.Id, me?.Id);
    }

    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@email.com";
}
