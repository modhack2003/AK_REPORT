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
    public async Task AdministratorCanPrepareMinimalDraftsAndVersionDoctorAssetsWithoutChangingHistoricalReports()
    {
        var previous = Environment.GetEnvironmentVariable("AK_DB_CONNECTION");
        Environment.SetEnvironmentVariable("AK_DB_CONNECTION", f.RuntimeConnection);
        try
        {
            var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            var username = "qa_ux_admin_" + Guid.NewGuid().ToString("N")[..8];
            await f.Sessions.CreateUser(new CreateUserRequest { Username = username, Password = password, Role = "Administrator" }, f.Admin, default);
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            var login = await client.PostAsJsonAsync("/auth/login", new LoginRequest { Username = username, Password = password });
            login.EnsureSuccessStatusCode();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/cases")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/cases/search", new CaseSearchRequest())).StatusCode);
            var templates = (await client.GetFromJsonAsync<List<TemplateDefinition>>("/templates"))!;
            var doctor = Fixtures.Attribution(); doctor.DoctorId = Guid.Empty;
            var firstDoctorResponse = await client.PostAsJsonAsync("/doctors/versions", doctor); firstDoctorResponse.EnsureSuccessStatusCode();
            var firstDoctor = (await firstDoctorResponse.Content.ReadFromJsonAsync<DoctorVersion>())!;
            var profile = (await client.GetFromJsonAsync<DoctorVersion>("/doctors/versions/" + firstDoctor.Id))!;
            Assert.Equal(Fixtures.Png(), profile.SignaturePng); Assert.Equal(Fixtures.Png(), profile.StampPng);
            var patient = new PatientMetadata { Name = "SYNTHETIC MINIMAL UI ACCEPTANCE" };
            var caseResponse = await client.PostAsJsonAsync("/cases", new CreateCaseRequest { OperationId = Guid.NewGuid(), Patient = patient });
            caseResponse.EnsureSuccessStatusCode(); var caseData = (await caseResponse.Content.ReadFromJsonAsync<CaseSummary>())!;
            var createResponse = await client.PostAsJsonAsync("/reports", new CreateReportRequest { OperationId = Guid.NewGuid(), CaseId = caseData.Id,
                Draft = new ReportDraft { TemplateVersionId = templates.First(t => t.ReportTypeCode == "CBC").VersionId,
                    DoctorVersionId = firstDoctor.Id, Metadata = new ReportMetadata { Patient = patient, ReportTime = Fixtures.Time } } });
            createResponse.EnsureSuccessStatusCode(); var report = (await createResponse.Content.ReadFromJsonAsync<ReportRevision>())!;
            Assert.Null(report.Data.Metadata.Patient.Age); Assert.Equal("", report.Data.Metadata.Patient.AgeUnit);
            Assert.Equal("", report.Data.Metadata.ClinicalHistory); Assert.Null(report.Data.Metadata.CollectionTime); Assert.Empty(report.Data.Results);
            var save = await client.PostAsJsonAsync($"/reports/{report.ReportId}/revisions", new SaveRevisionRequest { ExpectedRevision = 1, Draft = report.Data });
            save.EnsureSuccessStatusCode(); var saved = (await save.Content.ReadFromJsonAsync<ReportRevision>())!;
            Assert.Equal("Draft updated", saved.Reason); Assert.Equal(2, saved.Number);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/cases/{caseData.Id}/reports")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/reports/{report.ReportId}/history")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/reports/{report.ReportId}/preview")).StatusCode);
            var generated = await client.PostAsJsonAsync($"/reports/{report.ReportId}/documents/pdf?revision=1", new { }); generated.EnsureSuccessStatusCode();
            var document = (await generated.Content.ReadFromJsonAsync<GeneratedDocumentInfo>())!;
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/documents/" + document.Id)).StatusCode);
            profile.DisplayName = "SYNTHETIC UPDATED ATTRIBUTION"; profile.StampPng = null;
            var update = await client.PostAsJsonAsync("/doctors/versions", profile); update.EnsureSuccessStatusCode();
            var updated = (await update.Content.ReadFromJsonAsync<DoctorVersion>())!;
            Assert.Equal(firstDoctor.DoctorId, updated.DoctorId); Assert.NotEqual(firstDoctor.Id, updated.Id);
            Assert.Equal(firstDoctor.Id, (await client.GetFromJsonAsync<ReportRevision>($"/reports/{report.ReportId}?revision=1"))!.Data.DoctorVersionId);
            var original = (await client.GetFromJsonAsync<DoctorVersion>("/doctors/versions/" + firstDoctor.Id))!;
            Assert.Equal(Fixtures.Png(), original.StampPng);
            var repeat = await client.PostAsJsonAsync($"/reports/{report.ReportId}/documents/pdf?revision=1", new { }); repeat.EnsureSuccessStatusCode();
            Assert.Equal(document.Sha256, (await repeat.Content.ReadFromJsonAsync<GeneratedDocumentInfo>())!.Sha256);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/reports/{report.ReportId}/issue", new IssueReportRequest { ExpectedRevision = 2, Reason = "Synthetic issue attempt", AuthorizationBasis = "Synthetic test" })).StatusCode);
            var writerLogin = await client.PostAsJsonAsync("/auth/login", new LoginRequest { Username = "qa_writer", Password = f.WriterPassword });
            writerLogin.EnsureSuccessStatusCode(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await writerLogin.Content.ReadFromJsonAsync<LoginResponse>())!.Token);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/doctors/versions/" + updated.Id)).StatusCode);
            var library = (await client.GetFromJsonAsync<List<DoctorVersion>>("/doctors"))!;
            Assert.All(library, d => { Assert.Null(d.SignaturePng); Assert.Null(d.StampPng); });
        }
        finally { Environment.SetEnvironmentVariable("AK_DB_CONNECTION", previous); }
    }
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
