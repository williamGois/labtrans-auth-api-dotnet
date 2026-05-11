namespace AuthApi.Exceptions;

public sealed class DuplicateEmailException(string message) : Exception(message);

public sealed class InvalidCredentialsException(string message) : Exception(message);
