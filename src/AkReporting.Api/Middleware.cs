using AkReporting.Application;
using AkReporting.Domain;
using Npgsql;

namespace AkReporting.Api;

public sealed class SessionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IUserSessions sessions)
    {
        if (context.Request.Path is { Value: "/auth/login" or "/health/live" }) { await next(context); return; }
        var actor = await sessions.Authenticate(Token(context), context.RequestAborted);
        if (actor == null) { context.Response.StatusCode = 401; return; }
        context.Items["actor"] = actor;
        await next(context);
    }
    public static string Token(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.Ordinal) ? header[7..] : "";
    }
    public static Actor Actor(HttpContext context) => (Actor)context.Items["actor"]!;
    public static Actor RequireRole(HttpContext context, params string[] roles)
    {
        var actor = Actor(context);
        if (!roles.Contains(actor.Role, StringComparer.Ordinal)) throw new UnauthorizedAccessException("Permission denied.");
        return actor;
    }
}
public sealed class ProblemMiddleware(RequestDelegate next, ILogger<ProblemMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception e)
        {
            if (context.Response.HasStarted) throw;
            var status = e switch
            {
                ValidationException or BadHttpRequestException => 400,
                System.Text.Json.JsonException => 400,
                NotFoundException => 404, ConflictException => 409, UnauthorizedAccessException => 403,
                PostgresException { SqlState: "23505" } => 409,
                NpgsqlException => 503, _ => 500
            };
            var message = e is ValidationException or ConflictException or NotFoundException ? e.Message : status switch
            {
                400 => "Invalid request.", 403 => "Permission denied.", 409 => "A conflicting record already exists.",
                503 => "Database is unavailable. Saved revisions remain in the database; retry after service recovery.",
                _ => "The operation failed. Use the correlation ID when contacting support."
            };
            logger.LogWarning("Operation failed: {ErrorType}, status {Status}, correlation {Correlation}", e.GetType().Name, status, context.TraceIdentifier);
            context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(new { title = message, status, correlationId = context.TraceIdentifier });
        }
    }
}
