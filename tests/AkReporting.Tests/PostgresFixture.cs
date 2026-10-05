using System.Security.Cryptography;
using AkReporting.Application;
using AkReporting.Contracts;
using AkReporting.Domain;
using AkReporting.Infrastructure;
using AkReporting.Rendering;
using Npgsql;

namespace AkReporting.Tests;

public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("AK_TEST_PG") == null) Skip = "Set AK_TEST_PG to an isolated PostgreSQL administrative connection.";
    }
}
[CollectionDefinition("Postgres", DisableParallelization = true)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;

public sealed class MutableClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = Fixtures.Time;
    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
}
public sealed class PostgresFixture : IAsyncLifetime
{
    public Database Db { get; private set; } = null!;
    public PostgresCatalog Catalog { get; private set; } = null!;
    public PostgresReports Reports { get; private set; } = null!;
    public PostgresSessions Sessions { get; private set; } = null!;
    public PostgresDocuments Documents { get; private set; } = null!;
    public ReportService Service { get; private set; } = null!;
    public MutableClock Clock { get; } = new();
    public Actor Admin { get; private set; } = null!;
    public Actor Writer { get; private set; } = null!;
    public Actor Reviewer { get; private set; } = null!;
    public string RuntimeConnection { get; private set; } = "";
    public string OwnerConnection { get; private set; } = "";
    public string WriterPassword { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    private string master = "";
    private string databaseName = "";
    private string roleName = "";
    public async Task InitializeAsync()
    {
        master = Environment.GetEnvironmentVariable("AK_TEST_PG") ?? "";
        if (master.Length == 0) return;
        databaseName = "ak_test_" + Guid.NewGuid().ToString("N");
        roleName = "ak_app_" + Guid.NewGuid().ToString("N");
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using var owner = new NpgsqlConnection(master); await owner.OpenAsync();
        // Names/password are generated hex only, never supplied by a user.
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {databaseName}", owner)) await create.ExecuteNonQueryAsync();
        await using (var createRole = new NpgsqlCommand($"CREATE ROLE {roleName} LOGIN PASSWORD '{password}'", owner)) await createRole.ExecuteNonQueryAsync();
        var builder = new NpgsqlConnectionStringBuilder(master) { Database = databaseName };
        OwnerConnection = builder.ConnectionString;
        await Database.Migrate(OwnerConnection);
        await Database.Migrate(OwnerConnection); // checksum-aware no-op repeat
        await using (var c = new NpgsqlConnection(OwnerConnection))
        {
            await c.OpenAsync();
            var sql = (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "grant-runtime.sql"))).Replace(":\"runtime_role\"", roleName, StringComparison.Ordinal);
            await using var grant = new NpgsqlCommand(sql, c); await grant.ExecuteNonQueryAsync();
        }
        builder.Username = roleName; builder.Password = password;
        RuntimeConnection = builder.ConnectionString;
        Db = new Database(RuntimeConnection);
        Catalog = new PostgresCatalog(Db, Clock); Reports = new PostgresReports(Db, Clock);
        Sessions = new PostgresSessions(Db, Clock); Documents = new PostgresDocuments(Db, Clock);
        Service = new ReportService(Reports, Catalog, new ReportRenderer(), Documents);
        var id = await Sessions.CreateUser(new CreateUserRequest { Username = "qa_admin", Password = password, Role = "Administrator" }, null, default);
        Admin = new Actor(id, "Administrator");
        id = await Sessions.CreateUser(new CreateUserRequest { Username = "qa_writer", Password = WriterPassword, Role = "Writer" }, Admin, default);
        Writer = new Actor(id, "Writer");
        id = await Sessions.CreateUser(new CreateUserRequest { Username = "qa_reviewer", Password = password, Role = "MedicalReviewer" }, Admin, default);
        Reviewer = new Actor(id, "MedicalReviewer");
        foreach (var t in CandidateTemplates.All()) await Catalog.AddTemplate(t, Admin, default);
    }
    public async Task<(CaseSummary Case, ReportRevision Report)> NewReport(string code = "CBC")
    {
        var data = Fixtures.Revision(Fixtures.Template(code)).Data;
        var c = await Reports.CreateCase(new CreateCaseRequest { OperationId = Guid.NewGuid(), Patient = data.Metadata.Patient }, Writer, default);
        var r = await Service.Create(new CreateReportRequest { OperationId = Guid.NewGuid(), CaseId = c.Id, Draft = data }, Writer, default);
        return (c, r);
    }
    public async Task DisposeAsync()
    {
        if (master.Length == 0) return;
        if (Db != null) await Db.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var c = new NpgsqlConnection(master); await c.OpenAsync();
        await using (var dropDb = new NpgsqlCommand($"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE)", c)) await dropDb.ExecuteNonQueryAsync();
        await using (var dropRole = new NpgsqlCommand($"DROP ROLE IF EXISTS {roleName}", c)) await dropRole.ExecuteNonQueryAsync();
    }
}
