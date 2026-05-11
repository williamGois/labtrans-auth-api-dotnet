using System.Diagnostics;
using System.Security.Claims;
using AuthApi.Dtos;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace AuthApi.Observability;

public static class ObservabilityMiddlewareExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var correlationId = CorrelationContext.Ensure(context);
            context.Response.OnStarting(() =>
            {
                context.Response.Headers[CorrelationContext.HeaderName] = correlationId;
                return Task.CompletedTask;
            });

            await next();
        });
    }

    public static IApplicationBuilder UseRequestTelemetry(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var logger = context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("RequestTelemetry");
            var environment = context.RequestServices
                .GetRequiredService<IHostEnvironment>()
                .EnvironmentName;
            var method = context.Request.Method;
            var route = context.Request.Path.Value ?? "/";
            var inProgressRoute = route;
            var stopwatch = Stopwatch.StartNew();
            var correlationId = CorrelationContext.Get(context);

            AuthMetrics.HttpRequestsInProgress.WithLabels(method, inProgressRoute, AuthMetrics.ServiceName).Inc();

            using (logger.BeginScope(new Dictionary<string, object?>
            {
                ["serviceName"] = AuthMetrics.ServiceName,
                ["environment"] = environment,
                ["correlationId"] = correlationId,
                ["method"] = method,
                ["path"] = context.Request.Path.Value
            }))
            {
                try
                {
                    await next();
                }
                finally
                {
                    stopwatch.Stop();
                    route = ResolveRoute(context) ?? route;
                    var statusCode = context.Response.StatusCode.ToString();
                    var elapsedMs = stopwatch.Elapsed.TotalMilliseconds;
                    var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? context.User.FindFirstValue("sub");

                    AuthMetrics.HttpRequestsInProgress.WithLabels(method, inProgressRoute, AuthMetrics.ServiceName).Dec();
                    AuthMetrics.HttpRequestsTotal.WithLabels(method, route, statusCode, AuthMetrics.ServiceName).Inc();
                    AuthMetrics.HttpRequestDurationSeconds
                        .WithLabels(method, route, statusCode, AuthMetrics.ServiceName)
                        .Observe(stopwatch.Elapsed.TotalSeconds);

                    logger.LogInformation(
                        "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs} ms. route={Route} userId={UserId}",
                        method,
                        context.Request.Path.Value,
                        context.Response.StatusCode,
                        elapsedMs,
                        route,
                        userId);
                }
            }
        });
    }

    public static IApplicationBuilder UseStandardExceptionHandling(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            try
            {
                await next();
            }
            catch (Exception exception)
            {
                var logger = context.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("UnhandledException");

                using (logger.BeginScope(new Dictionary<string, object?>
                {
                    ["serviceName"] = AuthMetrics.ServiceName,
                    ["environment"] = context.RequestServices.GetRequiredService<IHostEnvironment>().EnvironmentName,
                    ["correlationId"] = CorrelationContext.Get(context),
                    ["method"] = context.Request.Method,
                    ["path"] = context.Request.Path.Value
                }))
                {
                    logger.LogError(exception, "Unhandled error while processing request.");
                }

                if (context.Response.HasStarted)
                {
                    throw;
                }

                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/problem+json";
                var problem = ApiErrorFactory.Create(
                    context,
                    StatusCodes.Status500InternalServerError,
                    "Unexpected error",
                    "Ocorreu um erro inesperado ao processar a requisicao.");
                await context.Response.WriteAsJsonAsync(problem);
            }
        });
    }

    private static string? ResolveRoute(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        var action = endpoint?.Metadata.GetMetadata<ControllerActionDescriptor>();
        if (action is not null)
        {
            return $"{action.ControllerName}.{action.ActionName}";
        }

        return endpoint?.DisplayName;
    }
}
