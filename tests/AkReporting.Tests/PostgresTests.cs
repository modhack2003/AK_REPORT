using AkReporting.Application;
using AkReporting.Contracts;
using AkReporting.Domain;
using AkReporting.Infrastructure;
using AkReporting.Rendering;
using Npgsql;

namespace AkReporting.Tests;

[Collection("Postgres")]
public sealed class PostgresTests(PostgresFixture f)
{
    [IntegrationFact]
    public async Task CaseGroupingAndNumberingRemainUniqueAcrossSimultaneousWritersAndRetries()
    {
        var data = Fixtures.Revision(Fixtures.Template("CBC")).Data;
        var request = new CreateCaseRequest { OperationId = Guid.NewGuid(), Patient = data.Metadata.Patient };
        var cases = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => f.Reports.CreateCase(request, f.Writer, default)));
        Assert.Single(cases.Select(c => c.Id).Distinct());
        var created = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => f.Service.Create(new CreateReportRequest
            { OperationId = Guid.NewGuid(), CaseId = cases[0].Id, Draft = Fixtures.Revision(Fixtures.Template(i % 2 == 0 ? "CBC" : "LFT")).Data }, f.Writer, default)));
        Assert.Equal(10, created.Select(r => r.PublicNumber).Distinct().Count());
        Assert.Equal(10, (await f.Reports.Reports(cases[0].Id, default)).Count);
        Assert.Contains(await f.Reports.SearchCases(new CaseSearchRequest { Query = created[0].PublicNumber }, default), c => c.Id == cases[0].Id);
        Assert.Empty(await f.Reports.SearchCases(new CaseSearchRequest { Query = "a nonexistent synthetic identifier" }, default));
        Assert.All(created, r => Assert.Matches("^AKDC-2026-[0-9]{6}$", r.PublicNumber));
        var op = new CreateReportRequest { OperationId = Guid.NewGuid(), CaseId = cases[0].Id, Draft = data };
        var first = await f.Service.Create(op, f.Writer, default); var repeated = await f.Service.Create(op, f.Writer, default);
        Assert.Equal(first.Id, repeated.Id);
        op.Draft.Metadata.ClinicalHistory = "Changed retry payload";
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.Create(op, f.Writer, default));
    }
    [IntegrationFact]
    public async Task ConcurrentCorrectionsHaveOneWinnerAndKeepCompleteHistoricalResults()
    {
        var (_, first) = await f.NewReport();
        async Task<ReportRevision?> Correct(decimal value)
        {
            var draft = Database.FromJson<ReportDraft>(Database.Json(first.Data));
            draft.Results[0].NumericValue = value;
            try { return await f.Service.Save(first.ReportId, new SaveRevisionRequest { ExpectedRevision = 1, Reason = "Synthetic concurrent correction", Draft = draft }, f.Writer, default); }
            catch (ConflictException) { return null; }
        }
        var saves = await Task.WhenAll(Correct(1.25m), Correct(2.50m));
        var winner = Assert.Single(saves, r => r != null);
        var original = await f.Reports.Revision(first.ReportId, 1, default);
        Assert.Equal(3.125m, original.Data.Results.Single(r => r.FieldCode == "hemoglobin").NumericValue);
        var history = await f.Reports.History(first.ReportId, default);
        Assert.Equal(2, history.Count); Assert.Equal(original.Id, winner!.PreviousRevisionId);
        Assert.Contains("results/counts/hemoglobin/0/NumericValue", winner.ChangedPaths);
        Assert.Equal(13, winner.Data.Results.Count);
    }
    [IntegrationFact]
    public async Task VersionPinsIssueAndCorrectionSurviveCatalogChangesAndDoctorDeactivation()
    {
        var (_, first) = await f.NewReport("ECG");
        await Assert.ThrowsAsync<ValidationException>(() => f.Service.Issue(first.ReportId,
            new IssueReportRequest { ExpectedRevision = 1, Reason = "QA", AuthorizationBasis = "Synthetic test" }, f.Writer, default));
        var doctor = await f.Catalog.AddDoctorVersion(Fixtures.Attribution(), f.Admin, default);
        var template = Fixtures.Template("ECG"); template.VersionId = Guid.NewGuid(); template.Version = 2;
        template.ReviewStatus = ReviewStatus.Approved; template.ReviewEvidence = "Synthetic test approval only, never a center clinical approval";
        template.ReviewedBy = "Synthetic QA reviewer"; template.ReviewedAt = f.Clock.GetUtcNow(); template.Sections[0].Fields[0].Required = true;
        await f.Catalog.AddTemplate(template, f.Reviewer, default);
        var data = first.Data; data.TemplateVersionId = template.VersionId; data.DoctorVersionId = doctor.Id;
        var prepared = await f.Service.Save(first.ReportId, new SaveRevisionRequest { ExpectedRevision = 1, Reason = "QA config version", Draft = data }, f.Writer, default);
        var issued = await f.Service.Issue(first.ReportId, new IssueReportRequest { ExpectedRevision = prepared.Number, Reason = "Synthetic issue test", AuthorizationBasis = "Synthetic process evidence" }, f.Writer, default);
        Assert.Equal(ReportState.Issued, issued.State);
        var originalPlan = await f.Service.Preview(first.ReportId, issued.Number, f.Writer, default);
        Assert.DoesNotContain(originalPlan.Pages.SelectMany(p => p.Text), t => t.Text.Contains("DRAFT", StringComparison.Ordinal));
        var updated = Fixtures.Attribution(); updated.DoctorId = doctor.DoctorId; updated.DisplayName = "Changed synthetic attribution";
        await f.Catalog.AddDoctorVersion(updated, f.Admin, default);
        await f.Catalog.SetDoctorActive(doctor.DoctorId, false, f.Admin, default);
        await Assert.ThrowsAsync<ValidationException>(() => f.Reports.Append(first.ReportId, issued.Number, issued.Data,
            "Synthetic inactive attribution test", ReportState.Draft, "", f.Writer, default));
        var historic = await f.Service.Preview(first.ReportId, issued.Number, f.Writer, default);
        Assert.Equal(Database.Json(originalPlan), Database.Json(historic));
        await Assert.ThrowsAsync<ValidationException>(() => f.Service.Save(first.ReportId, new SaveRevisionRequest { ExpectedRevision = issued.Number, Reason = "QA", Draft = issued.Data }, f.Writer, default));
        await f.Catalog.SetDoctorActive(doctor.DoctorId, true, f.Admin, default);
        issued.Data.Metadata.Patient.Name = "Corrected synthetic patient header";
        var corrected = await f.Service.Save(first.ReportId, new SaveRevisionRequest { ExpectedRevision = issued.Number, Reason = "Synthetic header correction", Draft = issued.Data }, f.Writer, default);
        Assert.Equal(ReportState.Draft, corrected.State);
        Assert.NotEqual(corrected.Data.Metadata.Patient.Name, (await f.Reports.Revision(first.ReportId, issued.Number, default)).Data.Metadata.Patient.Name);
        var pdf = await f.Service.Generate(first.ReportId, issued.Number, "pdf", f.Writer, default);
        var repeat = await f.Service.Generate(first.ReportId, issued.Number, "pdf", f.Writer, default);
        Assert.Equal(pdf.Id, repeat.Id); Assert.Equal(pdf.Sha256, Database.Hash((await f.Documents.Get(pdf.Id, default)).Bytes));
    }
    [IntegrationFact]
    public async Task LeastPrivilegeAndDatabaseTriggersRejectHistoricalMutation()
    {
        var (_, r) = await f.NewReport();
        await using var runtime = await f.Db.Source.OpenConnectionAsync();
        await using (var update = Database.Command(runtime, null, "UPDATE report_revision SET reason='overwrite' WHERE id=@id", ("id", r.Id)))
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() => update.ExecuteNonQueryAsync()); Assert.Equal("42501", exception.SqlState);
        }
        await using (var create = new NpgsqlCommand("CREATE TABLE unwanted_table(id int)", runtime))
            Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => create.ExecuteNonQueryAsync())).SqlState);
        await using var owner = new NpgsqlConnection(f.OwnerConnection); await owner.OpenAsync();
        await using var privileged = Database.Command(owner, null, "UPDATE report_revision SET reason='overwrite' WHERE id=@id", ("id", r.Id));
        Assert.Equal("55000", (await Assert.ThrowsAsync<PostgresException>(() => privileged.ExecuteNonQueryAsync())).SqlState);
        Assert.Equal("Initial creation", (await f.Reports.Revision(r.ReportId, 1, default)).Reason);
    }
    [IntegrationFact]
    public async Task SessionsHashTokensExpireOnIdleLockoutAndRevokeOnLogout()
    {
        f.Clock.Now = Fixtures.Time;
        var login = await f.Sessions.Login(new LoginRequest { Username = "qa_writer", Password = f.WriterPassword }, default);
        Assert.NotNull(login); Assert.NotNull(await f.Sessions.Authenticate(login.Token, default));
        await using (var c = await f.Db.Source.OpenConnectionAsync())
        await using (var cmd = new NpgsqlCommand("SELECT token_hash FROM user_session WHERE user_id='" + f.Writer.Id + "'", c))
            Assert.NotEqual(login.Token, await cmd.ExecuteScalarAsync());
        f.Clock.Now = f.Clock.Now.AddMinutes(16); Assert.Null(await f.Sessions.Authenticate(login.Token, default));
        login = await f.Sessions.Login(new LoginRequest { Username = "qa_writer", Password = f.WriterPassword }, default);
        await f.Sessions.Logout(login!.Token, default); Assert.Null(await f.Sessions.Authenticate(login.Token, default));
        for (var n = 0; n < 5; n++) Assert.Null(await f.Sessions.Login(new LoginRequest { Username = "qa_writer", Password = "Incorrect synthetic password" }, default));
        Assert.Null(await f.Sessions.Login(new LoginRequest { Username = "qa_writer", Password = f.WriterPassword }, default));
        f.Clock.Now = f.Clock.Now.AddMinutes(16);
        Assert.NotNull(await f.Sessions.Login(new LoginRequest { Username = "qa_writer", Password = f.WriterPassword }, default));
    }
    [IntegrationFact]
    public async Task RendererFailureLeavesCommittedRevisionAndRetentionNeverDeletesHistory()
    {
        var (_, r) = await f.NewReport("HISTO");
        await Assert.ThrowsAsync<ValidationException>(() => f.Service.Generate(r.ReportId, null, "invalid-format", f.Writer, default));
        await f.Reports.SetRetention(r.ReportId, RetentionState.Expired, f.Admin, default);
        Assert.Equal(r.Id, (await f.Reports.Revision(r.ReportId, 1, default)).Id);
        var pdf = await f.Service.Generate(r.ReportId, 1, "pdf", f.Writer, default);
        Assert.NotEmpty((await f.Documents.Get(pdf.Id, default)).Bytes);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.Preview(r.ReportId, 1, new Actor(Guid.NewGuid(), "Receptionist"), default));
    }
}
