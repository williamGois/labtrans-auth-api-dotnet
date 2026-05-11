namespace AuthApi.Observability;

public static class CorrelationContext
{
    public const string HeaderName = "X-Correlation-ID";
    private const string ItemName = "CorrelationId";
    private const int MaxLength = 128;

    public static string Ensure(HttpContext context)
    {
        if (context.Items.TryGetValue(ItemName, out var existing)
            && existing is string value
            && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var correlationId = ReadHeader(context) ?? Guid.NewGuid().ToString("N");
        context.Items[ItemName] = correlationId;
        return correlationId;
    }

    public static string Get(HttpContext context) => Ensure(context);

    private static string? ReadHeader(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(HeaderName, out var values))
        {
            return null;
        }

        var candidate = values.FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        return candidate.Length <= MaxLength ? candidate : candidate[..MaxLength];
    }
}
