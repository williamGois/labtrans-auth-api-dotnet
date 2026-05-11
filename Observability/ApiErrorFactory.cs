using AuthApi.Dtos;

namespace AuthApi.Observability;

public static class ApiErrorFactory
{
    public static ApiErrorResponse Create(
        HttpContext context,
        int status,
        string title,
        string detail)
    {
        return new ApiErrorResponse(
            title,
            status,
            detail,
            CorrelationContext.Get(context),
            DateTime.UtcNow);
    }
}
