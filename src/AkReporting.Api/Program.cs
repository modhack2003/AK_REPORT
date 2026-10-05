using System.Net;
using System.Threading.RateLimiting;
using AkReporting.Api;
using AkReporting.Application;
using AkReporting.Contracts;
using AkReporting.Domain;
using AkReporting.Infrastructure;
using AkReporting.Rendering;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 8 * 1024 * 1024);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.MaxDepth = 32;
    options.SerializerOptions.RespectNullableAnnotations = true;
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(_ => new Database(Environment.GetEnvironmentVariable("AK_DB_CONNECTION")
    ?? throw new InvalidOperationException("AK_DB_CONNECTION is required. Use protected service configuration.")));
builder.Services.AddScoped<ICatalogRepository, PostgresCatalog>();
builder.Services.AddScoped<IReportRepository, PostgresReports>();
builder.Services.AddScoped<IDocumentRepository, PostgresDocuments>();
builder.Services.AddScoped<IUserSessions, PostgresSessions>();
builder.Services.AddSingleton<IReportRenderer, ReportRenderer>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
if (args.Contains("--migrate"))
{
    await Database.Migrate(Environment.GetEnvironmentVariable("AK_MIGRATION_CONNECTION")
        ?? throw new InvalidOperationException("AK_MIGRATION_CONNECTION is required for migrations."));
    Console.WriteLine("Database migrations applied.");
    return;
}
if (args.Contains("--bootstrap-admin"))
{
    Console.Write("Administrator username: ");
    var username = Console.ReadLine() ?? "";
    Console.Write("Password (12–256 characters): ");
    var password = new System.Text.StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(true);
        if (key.Key == ConsoleKey.Enter) break;
        if (key.Key == ConsoleKey.Backspace) { if (password.Length > 0) password.Length--; }
        else if (!char.IsControl(key.KeyChar) && password.Length < 256) password.Append(key.KeyChar);
    }
    Console.WriteLine();
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<IUserSessions>().CreateUser(new CreateUserRequest
        { Username = username, Password = password.ToString(), Role = "Administrator" }, null, default);
    password.Clear();
    Console.WriteLine("First administrator created. No credentials were seeded.");
    return;
}
app.UseMiddleware<ProblemMiddleware>();
app.Use(async (context, next) =>
{
    var localDevelopment = app.Environment.IsDevelopment() && context.Connection.RemoteIpAddress is IPAddress address && IPAddress.IsLoopback(address);
    if (!context.Request.IsHttps && !localDevelopment)
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsJsonAsync(new { title = "HTTPS is required.", status = 400 });
        return;
    }
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    await next(context);
});
app.UseRateLimiter();
app.UseMiddleware<SessionMiddleware>();
app.MapGet("/health/live", () => Results.Ok(new { status = "running", clinicalStatus = "Requires center approval" }));
app.MapPost("/auth/login", async (LoginRequest request, IUserSessions sessions, CancellationToken ct) =>
{
    var login = await sessions.Login(request, ct);
    return login == null ? Results.Unauthorized() : Results.Ok(login);
}).RequireRateLimiting("login");
app.MapPost("/auth/logout", async (HttpContext context, IUserSessions sessions, CancellationToken ct) =>
{
    await sessions.Logout(SessionMiddleware.Token(context), ct);
    return Results.NoContent();
});
app.MapGet("/auth/activity", () => Results.Ok(new { active = true }));
app.MapReportEndpoints();
app.MapCatalogEndpoints();
app.Run();

public partial class Program;
