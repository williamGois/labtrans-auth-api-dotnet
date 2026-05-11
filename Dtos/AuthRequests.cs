using System.ComponentModel.DataAnnotations;

namespace AuthApi.Dtos;

public sealed record RegisterRequest(
    [Required, EmailAddress, MaxLength(320)] string Email,
    [Required, MinLength(6)] string Password);

public sealed record LoginRequest(
    [Required, EmailAddress, MaxLength(320)] string Email,
    [Required] string Password);
