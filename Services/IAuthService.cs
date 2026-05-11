using AuthApi.Dtos;

namespace AuthApi.Services;

public interface IAuthService
{
    Task<UserResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<UserResponse?> GetUserAsync(Guid userId, CancellationToken cancellationToken);
}
