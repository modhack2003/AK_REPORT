using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Text;
using AkReporting.Application;
using AkReporting.Contracts;
using AkReporting.Deployment;
using AkReporting.Domain;
using AkReporting.Infrastructure;
using Npgsql;

namespace AkReporting.WindowsSetup;

public sealed class InitialAccounts
{
    public string AdministratorName { get; set; } = "";
    public string AdministratorPassword { get; set; } = "";
    public string WriterName { get; set; } = "";
    public string WriterPassword { get; set; } = "";
    public byte[]? LogoPng { get; set; }
}

internal sealed class SetupReadinessException(string diagnostic)
    : InvalidOperationException("API host did not become ready with trusted HTTPS. " + diagnostic)
{
    public string Diagnostic { get; } = diagnostic;
}

internal sealed class Provisioner(string installRoot, Action<string> progress)
{
    private readonly string install = Path.GetFullPath(installRoot).TrimEnd(Path.DirectorySeparatorChar);
    private readonly string root = InstallationPaths.DataRoot;
    private string Stage = "Preflight";
    private void Step(string stage) { Stage = stage; progress(stage); }

    public async Task Initialize(InitialAccounts request)
    {
        WindowsSecurity.RequireAdministrator(); WindowsSecurity.RejectReparsePath(install);
        if (request.LogoPng != null) Branding.Validate(request.LogoPng);
        var hostExe = Path.Combine(install, "host", "AkReporting.Api.exe");
        var pgBin = Path.Combine(install, "postgres", "bin");
        if (!File.Exists(hostExe) || !File.Exists(Path.Combine(pgBin, "initdb.exe")))
            throw new InvalidOperationException("Install the full local-host package before running setup.");
        var cluster = Path.Combine(root, "postgres-data");
        if (!File.Exists(InstallationPaths.OwnerSettings(root)) && Directory.Exists(cluster) && Directory.EnumerateFileSystemEntries(cluster).Any())
            throw new InvalidOperationException("Existing database data has no matching protected owner configuration. Use the controlled restore procedure; setup will not replace it.");
        foreach (var name in new[] { InstallationPaths.HostService, InstallationPaths.DatabaseService })
            if (ServiceOperations.Exists(name)) ServiceOperations.CheckOwned(name, install);
        Step("Protecting installation configuration");
        WindowsSecurity.DirectoryAcl(root);
        WindowsSecurity.DirectoryAcl(Path.Combine(root, "administration"));
        var ownerPath = InstallationPaths.OwnerSettings(root);
        InstalledOwnerSettings owner;
        if (File.Exists(ownerPath))
        {
            owner = ProtectedConfiguration.Read<InstalledOwnerSettings>(ownerPath);
            if (!string.Equals(owner.InstallRoot, install, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Use the original application folder for repair/reinstallation.");
        }
        else
        {
            var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            owner = new InstalledOwnerSettings { InstallRoot = install, RuntimePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                OwnerConnection = Connection("ak_owner", password, "postgres") };
            ProtectedConfiguration.Write(ownerPath, owner);
        }
        Step("Registering dedicated Windows services");
        if (ServiceOperations.Exists(InstallationPaths.HostService)) ServiceOperations.Stop(InstallationPaths.HostService);
        await ServiceOperations.Ensure(InstallationPaths.DatabaseService,
            $"\"{Path.Combine(pgBin, "pg_ctl.exe")}\" runservice -N {InstallationPaths.DatabaseService} -D \"{cluster}\" -w", install);
        await ServiceOperations.Ensure(InstallationPaths.HostService, $"\"{hostExe}\" --installed-host", install, InstallationPaths.DatabaseService);
        WindowsSecurity.DirectoryAcl(root, ($"NT SERVICE\\{InstallationPaths.DatabaseService}", FileSystemRights.ReadAndExecute, false),
            ($"NT SERVICE\\{InstallationPaths.HostService}", FileSystemRights.ReadAndExecute, false));
        WindowsSecurity.DirectoryAcl(cluster, ($"NT SERVICE\\{InstallationPaths.DatabaseService}", FileSystemRights.Modify, true));
        var hostFolder = Path.Combine(root, "host");
        // Keep temporary DPAPI writes administrator-only; grant the service on the completed file below.
        WindowsSecurity.DirectoryAcl(hostFolder);
        if (!File.Exists(Path.Combine(cluster, "PG_VERSION")))
        {
            if (Directory.EnumerateFileSystemEntries(cluster).Any())
                throw new InvalidOperationException("Interrupted database initialization left nonempty data. Preserve it and consult the recovery guide; no files were deleted.");
            Step("Initializing isolated PostgreSQL cluster");
            var administration = Path.Combine(root, "administration");
            var passwordFolder = Path.Combine(administration, "initialization");
            var pwfile = Path.Combine(passwordFolder, "password.tmp");
            // initdb drops its Administrators membership before reading its password
            // file. Grant only traversal on parents and read access on an isolated
            // temporary folder; protected owner configuration stays administrator-only.
            using var initializer = System.Security.Principal.WindowsIdentity.GetCurrent();
            try
            {
                WindowsSecurity.DirectoryAcl(root, ($"NT SERVICE\\{InstallationPaths.DatabaseService}", FileSystemRights.ReadAndExecute, false),
                    ($"NT SERVICE\\{InstallationPaths.HostService}", FileSystemRights.ReadAndExecute, false),
                    (initializer.Name, FileSystemRights.Traverse, false));
                WindowsSecurity.DirectoryAcl(administration, (initializer.Name, FileSystemRights.Traverse, false));
                WindowsSecurity.DirectoryAcl(passwordFolder, (initializer.Name, FileSystemRights.ReadAndExecute, true));
                WindowsSecurity.DirectoryAcl(cluster, ($"NT SERVICE\\{InstallationPaths.DatabaseService}", FileSystemRights.Modify, true),
                    (initializer.Name, FileSystemRights.Modify, true));
                await File.WriteAllTextAsync(pwfile, new NpgsqlConnectionStringBuilder(owner.OwnerConnection).Password, new UTF8Encoding(false));
                await ServiceOperations.Tool(Path.Combine(pgBin, "initdb.exe"), "-D", cluster, "-U", "ak_owner", "--pwfile=" + pwfile,
                    "--encoding=UTF8", "--locale=C", "--auth-local=scram-sha-256", "--auth-host=scram-sha-256");
            }
            finally
            {
                if (File.Exists(pwfile)) File.Delete(pwfile);
                if (Directory.Exists(passwordFolder)) Directory.Delete(passwordFolder);
                WindowsSecurity.DirectoryAcl(administration);
                WindowsSecurity.DirectoryAcl(cluster, ($"NT SERVICE\\{InstallationPaths.DatabaseService}", FileSystemRights.Modify, true));
                WindowsSecurity.DirectoryAcl(root, ($"NT SERVICE\\{InstallationPaths.DatabaseService}", FileSystemRights.ReadAndExecute, false),
                    ($"NT SERVICE\\{InstallationPaths.HostService}", FileSystemRights.ReadAndExecute, false));
            }
        }
        else if ((await File.ReadAllTextAsync(Path.Combine(cluster, "PG_VERSION"))).Trim() != "17")
            throw new InvalidOperationException("This package cannot upgrade a different PostgreSQL major. Preserve the cluster and use a reviewed upgrade procedure.");
        // Finish configuration even if power was lost after initdb wrote PG_VERSION.
        // This dedicated package owns its local-only listener/auth fragment; no external cluster is edited.
        Step("Configuring isolated database listener");
        ServiceOperations.Stop(InstallationPaths.DatabaseService);
        var configuration = Path.Combine(cluster, "postgresql.conf");
        const string include = "include = 'ak-reporting.conf'";
        if (!(await File.ReadAllTextAsync(configuration)).Contains(include, StringComparison.Ordinal))
            await File.AppendAllTextAsync(configuration, "\n# Managed local reporting configuration\n" + include + "\n");
        await File.WriteAllTextAsync(Path.Combine(cluster, "ak-reporting.conf"),
            "listen_addresses = '127.0.0.1'\nport = 55432\npassword_encryption = 'scram-sha-256'\nlog_statement = 'none'\nlog_min_error_statement = 'panic'\n", new UTF8Encoding(false));
        await File.WriteAllTextAsync(Path.Combine(cluster, "pg_hba.conf"),
            "# Managed local service connections only; no trust authentication.\nhost all all 127.0.0.1/32 scram-sha-256\n", new UTF8Encoding(false));
        Step("Starting PostgreSQL and applying migrations");
        ServiceOperations.Start(InstallationPaths.DatabaseService);
        await WaitForDatabase(owner.OwnerConnection);
        await CreateDatabase(owner);
        var dbOwner = new NpgsqlConnectionStringBuilder(owner.OwnerConnection) { Database = "ak_reporting" }.ConnectionString;
        await Database.Migrate(dbOwner);
        await GrantRuntime(dbOwner);
        var runtime = Connection("ak_reporting_app", owner.RuntimePassword, "ak_reporting");
        Step("Provisioning chosen accounts and draft schemas");
        await ProvisionAccounts(runtime, request);
        if (request.LogoPng != null) { Step("Installing supplied centre logo"); Branding.Install(request.LogoPng); }
        Step("Configuring trusted local HTTPS");
        var settingsPath = InstallationPaths.HostSettings(root);
        var settings = File.Exists(settingsPath) ? ProtectedConfiguration.Read<InstalledHostSettings>(settingsPath) : new InstalledHostSettings();
        if (settings.RuntimeConnection.Length > 0 && settings.RuntimeConnection != runtime)
            throw new InvalidOperationException("Host configuration differs from the protected database owner configuration.");
        settings.RuntimeConnection = runtime;
        if (settings.ServerPfx.Length == 0 || settings.CertificateExpires <= DateTimeOffset.UtcNow.AddDays(30))
        {
            if (settings.RootCertificate.Length > 0) LocalCertificates.RemoveTrust(settings);
            LocalCertificates.Create(settings);
        }
        ProtectedConfiguration.Write(settingsPath, settings);
        WindowsSecurity.DirectoryAcl(hostFolder, ($"NT SERVICE\\{InstallationPaths.HostService}", FileSystemRights.ReadAndExecute, true));
        LocalCertificates.Trust(settings);
        Step("Starting API host and checking readiness");
        ServiceOperations.Start(InstallationPaths.HostService);
        await WaitForHost();
        await File.WriteAllTextAsync(Path.Combine(root, "administration", "provisioned.txt"),
            "Provisioned " + DateTimeOffset.UtcNow.ToString("O") + "\nEndpoint: https://localhost:7043\n");
        Step("Ready — sign in with the accounts you chose");
    }
    private async Task CreateDatabase(InstalledOwnerSettings owner)
    {
        await using var c = new NpgsqlConnection(owner.OwnerConnection); await c.OpenAsync();
        await using (var check = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname='ak_reporting'", c))
            if (await check.ExecuteScalarAsync() == null)
            { await using var create = new NpgsqlCommand("CREATE DATABASE ak_reporting", c); await create.ExecuteNonQueryAsync(); }
        await using (var check = new NpgsqlCommand("SELECT 1 FROM pg_roles WHERE rolname='ak_reporting_app'", c))
            if (await check.ExecuteScalarAsync() == null)
            {
                // Password is generated hexadecimal, not user input. Never logged or passed on a command line.
                await using var create = new NpgsqlCommand("CREATE ROLE ak_reporting_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD '" + owner.RuntimePassword + "'", c);
                await create.ExecuteNonQueryAsync();
            }
    }
    private static async Task GrantRuntime(string connection)
    {
        await using var c = new NpgsqlConnection(connection); await c.OpenAsync();
        var sql = (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "grant-runtime.sql"))).Replace(":\"runtime_role\"", "ak_reporting_app", StringComparison.Ordinal);
        await using var grants = new NpgsqlCommand(sql, c); await grants.ExecuteNonQueryAsync();
    }
    private async Task ProvisionAccounts(string connection, InitialAccounts request)
    {
        await using var db = new Database(connection);
        var sessions = new PostgresSessions(db, TimeProvider.System);
        var catalog = new PostgresCatalog(db, TimeProvider.System);
        await using var c = await db.Source.OpenConnectionAsync();
        await using var count = new NpgsqlCommand("SELECT COUNT(*) FROM app_user", c);
        var users = (long)(await count.ExecuteScalarAsync())!;
        if (users > 0 && File.Exists(Path.Combine(root, "administration", "provisioned.txt"))) return;
        if (request.AdministratorName.Equals(request.WriterName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Administrator and writer must be separate usernames.");
        if (string.IsNullOrWhiteSpace(request.AdministratorName) || string.IsNullOrWhiteSpace(request.WriterName))
            throw new InvalidOperationException("Enter chosen administrator/writer credentials to finish first-run setup.");
        Actor admin;
        if (users == 0)
        {
            var id = await sessions.CreateUser(new CreateUserRequest { Username = request.AdministratorName, Password = request.AdministratorPassword, Role = "Administrator" }, null, default);
            admin = new Actor(id, "Administrator");
        }
        else
        {
            var login = await sessions.Login(new LoginRequest { Username = request.AdministratorName, Password = request.AdministratorPassword }, default)
                ?? throw new InvalidOperationException("Enter the existing administrator credentials to resume setup.");
            var actor = await sessions.Authenticate(login.Token, default); await sessions.Logout(login.Token, default);
            admin = actor is { Role: "Administrator" } ? actor : throw new InvalidOperationException("An administrator account is required.");
        }
        await using var writer = Database.Command(c, null, "SELECT role_code FROM app_user WHERE username=@name", ("name", request.WriterName.ToLowerInvariant()));
        var existingRole = await writer.ExecuteScalarAsync();
        if (existingRole == null) await sessions.CreateUser(new CreateUserRequest { Username = request.WriterName, Password = request.WriterPassword, Role = "Writer" }, admin, default);
        else if ((string)existingRole != "Writer") throw new InvalidOperationException("The chosen writer username has another role.");
        var templates = await catalog.Templates(default);
        foreach (var draft in CandidateTemplates.All().Where(t => !templates.Any(e => e.ReportTypeCode == t.ReportTypeCode)))
            await catalog.AddTemplate(draft, admin, default);
    }
    public async Task Stop(bool remove)
    {
        WindowsSecurity.RequireAdministrator();
        foreach (var name in new[] { InstallationPaths.HostService, InstallationPaths.DatabaseService })
        {
            if (!ServiceOperations.Exists(name)) continue;
            ServiceOperations.CheckOwned(name, install); ServiceOperations.Stop(name);
            if (remove) await ServiceOperations.Tool("sc.exe", "delete", name);
        }
        if (remove && File.Exists(InstallationPaths.HostSettings(root)))
            LocalCertificates.RemoveTrust(ProtectedConfiguration.Read<InstalledHostSettings>(InstallationPaths.HostSettings(root)));
        progress(remove ? "Services removed; report database and recovery configuration preserved." : "Services stopped.");
    }
    public void RecordFailure(Exception error)
    {
        // No general exception messages, arguments, credentials, SQL or results.
        // initdb diagnostics are allowed only before application data exists.
        try
        {
            var summary = DateTimeOffset.UtcNow.ToString("O") + " | " + Stage + " | " + error.GetType().Name;
            if (error is SetupToolException tool)
            {
                summary += " | " + tool.Tool + " exit " + tool.ExitCode;
                if (tool.InitializationDiagnostics.Length > 0)
                    summary += "\n" + tool.InitializationDiagnostics[..Math.Min(tool.InitializationDiagnostics.Length, 4000)];
            }
            else if (error is SetupReadinessException readiness) summary += " | " + readiness.Diagnostic;
            File.AppendAllText(Path.Combine(root, "administration", "setup-status.log"), summary + "\n");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    public async Task Purge()
    {
        WindowsSecurity.RequireAdministrator(); WindowsSecurity.RejectReparsePath(root); WindowsSecurity.RejectReparsePath(install);
        if (File.Exists(InstallationPaths.OwnerSettings(root)))
        {
            var owner = ProtectedConfiguration.Read<InstalledOwnerSettings>(InstallationPaths.OwnerSettings(root));
            if (!string.Equals(owner.InstallRoot, install, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Complete removal must run from the original installation folder.");
        }
        // Check the entire owned tree before deleting. Never traverse a junction into another directory.
        if (Directory.Exists(root)) CheckRemovalTree(new DirectoryInfo(root));
        if (Directory.Exists(install)) CheckRemovalTree(new DirectoryInfo(install));
        await Stop(remove: true);
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        progress("Complete removal: local report database, accounts, configuration and logo deleted.");
    }
    private static void CheckRemovalTree(DirectoryInfo directory)
    {
        foreach (var item in directory.EnumerateFileSystemInfos())
        {
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Complete removal found a linked path. Remove the link or use controlled cleanup; no report data was deleted.");
            if (item is DirectoryInfo child) CheckRemovalTree(child);
        }
    }
    private static string Connection(string user, string password, string database) => new NpgsqlConnectionStringBuilder
    { Host = "127.0.0.1", Port = InstallationPaths.DatabasePort, Database = database, Username = user, Password = password, Timeout = 10, CommandTimeout = 30 }.ConnectionString;
    private static async Task WaitForDatabase(string connection)
    {
        for (var i = 0; i < 30; i++)
        {
            try { await using var c = new NpgsqlConnection(connection); await c.OpenAsync(); return; }
            catch (NpgsqlException) { await Task.Delay(1000); }
        }
        throw new InvalidOperationException("PostgreSQL did not become ready. Check service state and port 55432.");
    }
    private static async Task WaitForHost()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var diagnostic = "No response";
        for (var i = 0; i < 30; i++)
        {
            try
            {
                using var r = await client.GetAsync("https://localhost:7043/health/live");
                if (r.IsSuccessStatusCode) return;
                diagnostic = "HTTP " + (int)r.StatusCode;
            }
            catch (HttpRequestException error)
            {
                // Types/codes only, never exception messages or HTTP response bodies.
                diagnostic = error.HttpRequestError.ToString();
                for (Exception? cause = error; cause != null; cause = cause.InnerException)
                    diagnostic += " / " + cause.GetType().Name + " 0x" + cause.HResult.ToString("X8");
            }
            catch (TaskCanceledException) { diagnostic = "HTTPS request timeout"; }
            await Task.Delay(1000);
        }
        throw new SetupReadinessException(diagnostic);
    }
}
