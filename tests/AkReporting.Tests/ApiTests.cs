using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using AkReporting.Contracts;
using AkReporting.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AkReporting.Tests;

[Collection("Postgres")]
public sealed class ApiTests(PostgresFixture f)
{
    [IntegrationFact]
    public async Task AuthenticatedApiCompletesFiveSeparateReportsAndRejectsReceptionistResultAccess()
    {
        var previous = Environment.GetEnvironmentVariable("AK_DB_CONNECTION");
        Environment.SetEnvironmentVariable("AK_DB_CONNECTION", f.RuntimeConnection);
        try
        {
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/cases")).StatusCode);
            var loginResponse = await client.PostAsJsonAsync("/auth/login", new LoginRequest { Username = "qa_writer", Password = f.WriterPassword });
            loginResponse.EnsureSuccessStatusCode();
            var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
            var metadata = Fixtures.Revision(Fixtures.Template("CBC")).Data.Metadata;
            var create = await client.PostAsJsonAsync("/cases", new CreateCaseRequest { OperationId = Guid.NewGuid(), Patient = metadata.Patient });
            create.EnsureSuccessStatusCode(); var caseData = (await create.Content.ReadFromJsonAsync<CaseSummary>())!;
            foreach (var code in new[] { "CBC", "LFT", "URINE", "HISTO", "ECG" })
            {
                var reportResponse = await client.PostAsJsonAsync("/reports", new CreateReportRequest { OperationId = Guid.NewGuid(), CaseId = caseData.Id, Draft = Fixtures.Revision(Fixtures.Template(code)).Data });
                reportResponse.EnsureSuccessStatusCode();
                var report = (await reportResponse.Content.ReadFromJsonAsync<ReportRevision>())!;
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/reports/{report.ReportId}/preview")).StatusCode);
                var generated = await client.PostAsJsonAsync($"/reports/{report.ReportId}/documents/pdf", new { });
                generated.EnsureSuccessStatusCode(); var doc = (await generated.Content.ReadFromJsonAsync<GeneratedDocumentInfo>())!;
                var download = await client.GetAsync("/documents/" + doc.Id); download.EnsureSuccessStatusCode();
                Assert.Equal("application/pdf", download.Content.Headers.ContentType!.MediaType);
                Assert.Equal(doc.Sha256, Database.Hash(await download.Content.ReadAsByteArrayAsync()));
                var stale = await client.PostAsJsonAsync($"/reports/{report.ReportId}/revisions", new SaveRevisionRequest { ExpectedRevision = 0, Reason = "Synthetic stale request", Draft = report.Data });
                Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
                var issue = await client.PostAsJsonAsync($"/reports/{report.ReportId}/issue", new IssueReportRequest { ExpectedRevision = 1, Reason = "QA", AuthorizationBasis = "Synthetic evidence" });
                Assert.Equal(HttpStatusCode.BadRequest, issue.StatusCode);
            }
            var reports = (await client.GetFromJsonAsync<List<ReportSummary>>($"/cases/{caseData.Id}/reports"))!;
            Assert.Equal(5, reports.Count); Assert.Equal(5, reports.Select(r => r.PublicNumber).Distinct().Count());
            var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            var username = "qa_reception_" + Guid.NewGuid().ToString("N")[..8];
            await f.Sessions.CreateUser(new CreateUserRequest { Username = username, Password = password, Role = "Receptionist" }, f.Admin, default);
            var receptionLogin = await client.PostAsJsonAsync("/auth/login", new LoginRequest { Username = username, Password = password });
            var session = (await receptionLogin.Content.ReadFromJsonAsync<LoginResponse>())!;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/cases")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/cases/{caseData.Id}/reports")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/reports/{reports[0].Id}/preview")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/settings", new CenterSettings())).StatusCode);
            await client.PostAsJsonAsync("/auth/logout", new { });
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/cases")).StatusCode);
        }
        finally { Environment.SetEnvironmentVariable("AK_DB_CONNECTION", previous); }
    }
    [IntegrationFact]
    public async Task MalformedJsonAndNullContractsProduceSanitizedErrors()
    {
        var previous = Environment.GetEnvironmentVariable("AK_DB_CONNECTION"); Environment.SetEnvironmentVariable("AK_DB_CONNECTION", f.RuntimeConnection);
        try
        {
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            using var bad = new StringContent("{\"username\":null,\"password\":\"SENSITIVE-SYNTHETIC-MARKER\"}", System.Text.Encoding.UTF8, "application/json");
            var response = await client.PostAsync("/auth/login", bad);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("SENSITIVE-SYNTHETIC-MARKER", body); Assert.DoesNotContain("Npgsql", body);
        }
        finally { Environment.SetEnvironmentVariable("AK_DB_CONNECTION", previous); }
    }
}
