using System.Text.Json;
using AkReporting.Contracts;
using AkReporting.Domain;

namespace AkReporting.Application;

public sealed class ReportService(IReportRepository reports, ICatalogRepository catalog, IReportRenderer renderer, IDocumentRepository documents)
{
    public async Task<ReportRevision> Create(CreateReportRequest request, Actor actor, CancellationToken ct)
    {
        RequireWriter(actor);
        if (request.OperationId == Guid.Empty || request.CaseId == Guid.Empty) throw new ValidationException("Case and operation IDs are required.");
        var copy = Copy(request);
        ReportValidator.Draft(copy.Draft, await catalog.Template(copy.Draft.TemplateVersionId, ct));
        await CheckDoctor(copy.Draft, ct);
        return await reports.CreateReport(copy, actor, ct);
    }
    public async Task<ReportRevision> Save(Guid id, SaveRevisionRequest request, Actor actor, CancellationToken ct)
    {
        RequireWriter(actor);
        ReportValidator.Bounded(request.Reason, "Revision reason", 2000);
        var copy = Copy(request.Draft);
        var previous = await reports.Revision(id, null, ct);
        if (previous.State == ReportState.Issued) ReportValidator.Required(request.Reason, "Correction reason", 2000);
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? "Draft updated" : request.Reason;
        var oldTemplate = await catalog.Template(previous.Data.TemplateVersionId, ct);
        var template = await catalog.Template(copy.TemplateVersionId, ct);
        if (template.ReportTypeCode != oldTemplate.ReportTypeCode) throw new ValidationException("Cannot change the report family in a correction.");
        ReportValidator.Draft(copy, template);
        await CheckDoctor(copy, ct);
        return await reports.Append(id, request.ExpectedRevision, copy, reason, ReportState.Draft, "", actor, ct);
    }
    public async Task<ReportRevision> Issue(Guid id, IssueReportRequest request, Actor actor, CancellationToken ct)
    {
        RequireWriter(actor);
        ReportValidator.Required(request.Reason, "Issue reason", 2000);
        ReportValidator.Required(request.AuthorizationBasis, "Center authorization process/evidence", 4000);
        var previous = await reports.Revision(id, null, ct);
        if (previous.State == ReportState.Issued) throw new ConflictException("This revision is already issued.");
        ReportValidator.Draft(previous.Data, await catalog.Template(previous.Data.TemplateVersionId, ct), issuing: true);
        await CheckDoctor(previous.Data, ct);
        return await reports.Append(id, request.ExpectedRevision, previous.Data, request.Reason, ReportState.Issued, request.AuthorizationBasis, actor, ct);
    }
    public async Task<PagePlan> Preview(Guid id, int? number, Actor actor, CancellationToken ct)
    {
        RequireWriter(actor);
        var revision = await reports.Revision(id, number, ct);
        var template = await catalog.Template(revision.Data.TemplateVersionId, ct);
        var doctor = revision.Data.DoctorVersionId is Guid version ? await catalog.Doctor(version, ct) : null;
        return renderer.Plan(revision, template, doctor);
    }
    public async Task<GeneratedDocumentInfo> Generate(Guid id, int? number, string format, Actor actor, CancellationToken ct)
    {
        RequireWriter(actor);
        var revision = await reports.Revision(id, number, ct);
        var plan = await Preview(id, revision.Number, actor, ct);
        var doc = renderer.Render(plan, format, revision.CreatedAt);
        return await documents.Store(revision.Id, doc, actor, ct);
    }
    private async Task CheckDoctor(ReportDraft draft, CancellationToken ct)
    {
        if (draft.DoctorVersionId is not Guid id) return;
        var doctor = await catalog.Doctor(id, ct);
        if (!doctor.Active) throw new ValidationException("Selected doctor is inactive.");
    }
    public static void RequireWriter(Actor actor)
    {
        if (!actor.CanWrite) throw new UnauthorizedAccessException("Result-writing permission required.");
    }
    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}
