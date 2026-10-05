using System.Security.Cryptography;
using AkReporting.Application;
using AkReporting.Contracts;
using AkReporting.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using static AkReporting.Infrastructure.Database;

namespace AkReporting.Infrastructure;

public sealed class PostgresSessions(Database db, TimeProvider clock) : IUserSessions
{
    private static readonly PasswordHasher<string> Hasher = new(Options.Create(new PasswordHasherOptions { IterationCount = 600000 }));
    private static readonly string DummyHash = Hasher.HashPassword("unknown", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    public async Task<Guid> CreateUser(CreateUserRequest request, Actor? creator, CancellationToken ct)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(request.Username, "^[a-zA-Z0-9_.-]{3,64}$")) throw new ValidationException("Username must be 3–64 simple characters.");
        if (request.Password.Length is < 12 or > 256) throw new ValidationException("Password must be 12–256 characters.");
        if (request.Role is not ("Administrator" or "Writer" or "Receptionist" or "MedicalReviewer")) throw new ValidationException("Unknown role.");
        if (creator != null && creator.Role != "Administrator") throw new UnauthorizedAccessException();
        await using var c = await db.Source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await using (var guard = Command(c, tx, "SELECT pg_advisory_xact_lock(78261592)")) await guard.ExecuteNonQueryAsync(ct);
        if (creator == null)
        {
            await using var count = Command(c, tx, "SELECT COUNT(*) FROM app_user");
            if ((long)(await count.ExecuteScalarAsync(ct))! != 0 || request.Role != "Administrator")
                throw new ConflictException("Bootstrap is only permitted for the first administrator.");
        }
        var id = Guid.NewGuid();
        await using var cmd = Command(c, tx,
            "INSERT INTO app_user(id,username,password_hash,role_code) VALUES (@id,@name,@hash,@role)",
            ("id", id), ("name", request.Username.ToLowerInvariant()), ("hash", Hasher.HashPassword(request.Username, request.Password)), ("role", request.Role));
        await cmd.ExecuteNonQueryAsync(ct);
        await Audit(c, tx, creator ?? new Actor(id, "Administrator"), "user.create", id, null, ct);
        await tx.CommitAsync(ct);
        return id;
    }
    public async Task<LoginResponse?> Login(LoginRequest request, CancellationToken ct)
    {
        if (request.Username.Length > 64 || request.Password.Length > 256) return null;
        await using var c = await db.Source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await using var cmd = Command(c, tx, "SELECT id,password_hash,role_code,active,failed_attempts,locked_until FROM app_user WHERE username=@name FOR UPDATE", ("name", request.Username.ToLowerInvariant()));
        Guid id; string hash; string role; bool active; int attempts; DateTimeOffset? locked;
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct))
            {
                Hasher.VerifyHashedPassword("unknown", DummyHash, request.Password);
                return null;
            }
            id = r.GetGuid(0); hash = r.GetString(1); role = r.GetString(2); active = r.GetBoolean(3);
            attempts = r.GetInt32(4); locked = r.IsDBNull(5) ? null : r.GetFieldValue<DateTimeOffset>(5);
        }
        var now = clock.GetUtcNow();
        var verified = Hasher.VerifyHashedPassword(request.Username, hash, request.Password);
        if (!active || locked > now) return null;
        if (verified == PasswordVerificationResult.Failed)
        {
            if (locked.HasValue && locked <= now) attempts = 0;
            attempts++;
            await using var failed = Command(c, tx, "UPDATE app_user SET failed_attempts=@attempts,locked_until=@time WHERE id=@id", ("attempts", attempts), ("time", attempts >= 5 ? now.AddMinutes(15).UtcDateTime : null), ("id", id));
            await failed.ExecuteNonQueryAsync(ct);
            await Audit(c, tx, new Actor(id, role), "login.failed", id, null, ct);
            await tx.CommitAsync(ct);
            return null;
        }
        await using (var reset = Command(c, tx, "UPDATE app_user SET failed_attempts=0,locked_until=NULL,password_hash=@hash WHERE id=@id",
            ("hash", verified == PasswordVerificationResult.SuccessRehashNeeded ? Hasher.HashPassword(request.Username, request.Password) : hash), ("id", id))) await reset.ExecuteNonQueryAsync(ct);
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var expires = now.AddHours(8);
        await using (var session = Command(c, tx, "INSERT INTO user_session VALUES (@hash,@id,@time,@time,@expires,false)",
            ("hash", Hash(token)), ("id", id), ("time", now.UtcDateTime), ("expires", expires.UtcDateTime))) await session.ExecuteNonQueryAsync(ct);
        await Audit(c, tx, new Actor(id, role), "login.success", id, null, ct);
        await tx.CommitAsync(ct);
        return new LoginResponse { Token = token, ExpiresAt = expires, Role = role };
    }
    public async Task<Actor?> Authenticate(string token, CancellationToken ct)
    {
        if (token.Length != 44) return null;
        var now = clock.GetUtcNow();
        await using var c = await db.Source.OpenConnectionAsync(ct);
        await using var cmd = Command(c, null,
            "UPDATE user_session s SET last_seen=@time FROM app_user u WHERE s.token_hash=@hash AND u.id=s.user_id AND u.active AND NOT s.revoked AND s.expires_at>@time AND s.last_seen>@idle RETURNING u.id,u.role_code",
            ("time", now.UtcDateTime), ("hash", Hash(token)), ("idle", now.AddMinutes(-15).UtcDateTime));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? new Actor(r.GetGuid(0), r.GetString(1)) : null;
    }
    public async Task Logout(string token, CancellationToken ct)
    {
        await using var c = await db.Source.OpenConnectionAsync(ct);
        await using var cmd = Command(c, null, "UPDATE user_session SET revoked=true WHERE token_hash=@hash", ("hash", Hash(token)));
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
