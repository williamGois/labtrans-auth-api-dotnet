namespace AuthApi.Dtos;

public sealed record UserResponse(Guid Id, string Email, DateTime CreatedAt);

public sealed record AuthResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    UserResponse User);

public sealed record ErrorResponse(string Message);

public sealed record ApiErrorResponse(
    string Title,
    int Status,
    string Detail,
    string CorrelationId,
    DateTime Timestamp);
