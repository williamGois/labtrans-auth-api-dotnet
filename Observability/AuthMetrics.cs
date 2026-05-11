using Prometheus;

namespace AuthApi.Observability;

public static class AuthMetrics
{
    public const string ServiceName = "labtrans-auth-api-dotnet";

    public static readonly Counter HttpRequestsTotal = Metrics.CreateCounter(
        "http_requests_total",
        "Total HTTP requests processed by the Auth API.",
        new CounterConfiguration
        {
            LabelNames = ["method", "route", "status_code", "service"]
        });

    public static readonly Histogram HttpRequestDurationSeconds = Metrics.CreateHistogram(
        "http_request_duration_seconds",
        "HTTP request duration in seconds for the Auth API.",
        new HistogramConfiguration
        {
            LabelNames = ["method", "route", "status_code", "service"],
            Buckets = Histogram.ExponentialBuckets(0.005, 2, 12)
        });

    public static readonly Gauge HttpRequestsInProgress = Metrics.CreateGauge(
        "http_requests_in_progress",
        "HTTP requests currently in progress for the Auth API.",
        new GaugeConfiguration
        {
            LabelNames = ["method", "route", "service"]
        });

    public static readonly Counter RegisterSuccess = Metrics.CreateCounter(
        "auth_register_success_total",
        "Successful user registrations.");

    public static readonly Counter RegisterFailure = Metrics.CreateCounter(
        "auth_register_failure_total",
        "Failed user registrations.",
        new CounterConfiguration { LabelNames = ["error_type"] });

    public static readonly Counter LoginSuccess = Metrics.CreateCounter(
        "auth_login_success_total",
        "Successful logins.");

    public static readonly Counter LoginFailure = Metrics.CreateCounter(
        "auth_login_failure_total",
        "Failed logins.",
        new CounterConfiguration { LabelNames = ["error_type"] });

    public static readonly Counter JwtIssued = Metrics.CreateCounter(
        "auth_jwt_issued_total",
        "JWT access tokens issued by the Auth API.");

    public static readonly Counter InvalidCredentials = Metrics.CreateCounter(
        "auth_invalid_credentials_total",
        "Login attempts rejected by invalid credentials.");

    public static readonly Counter ProtectedRouteUnauthorized = Metrics.CreateCounter(
        "auth_protected_route_unauthorized_total",
        "Unauthorized requests rejected on protected Auth API routes.",
        new CounterConfiguration { LabelNames = ["error_type"] });
}
