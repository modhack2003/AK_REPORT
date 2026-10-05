using AkReporting.Contracts;
using AkReporting.Domain;

namespace AkReporting.Application;

public interface IReportRepository
{
    Task<CaseSummary> CreateCase(CreateCaseRequest request, Actor actor, CancellationToken ct);
    Task<IReadOnlyList<CaseSummary>> Cases(CancellationToken ct);
    Task<IReadOnlyList<CaseSummary>> SearchCases(CaseSearchRequest request, CancellationToken ct);
    Task<IReadOnlyList<ReportSummary>> Reports(Guid caseId, CancellationToken ct);
    Task<ReportRevision> CreateReport(CreateReportRequest request, Actor actor, CancellationToken ct);
    Task<ReportRevision> Revision(Guid reportId, int? number, CancellationToken ct);
    Task<IReadOnlyList<ReportRevision>> History(Guid reportId, CancellationToken ct);
    Task<ReportRevision> Append(Guid reportId, int expected, ReportDraft draft, string reason, ReportState state,
        string authorizationBasis, Actor actor, CancellationToken ct);
    Task SetRetention(Guid reportId, RetentionState state, Actor actor, CancellationToken ct);
}
public interface ICatalogRepository
{
    Task<IReadOnlyList<TemplateDefinition>> Templates(CancellationToken ct);
    Task<TemplateDefinition> Template(Guid id, CancellationToken ct);
    Task<TemplateDefinition> AddTemplate(TemplateDefinition definition, Actor actor, CancellationToken ct);
    Task<IReadOnlyList<DoctorVersion>> Doctors(CancellationToken ct);
    Task<DoctorVersion> Doctor(Guid id, CancellationToken ct);
    Task<DoctorVersion> AddDoctorVersion(DoctorVersion version, Actor actor, CancellationToken ct);
    Task SetDoctorActive(Guid doctorId, bool active, Actor actor, CancellationToken ct);
    Task<CenterSettings> Settings(CancellationToken ct);
    Task SetSettings(CenterSettings settings, Actor actor, CancellationToken ct);
}
public sealed record RenderedDocument(byte[] Bytes, string Format, PagePlan Plan);
public sealed record StoredDocument(GeneratedDocumentInfo Info, byte[] Bytes);
public interface IReportRenderer
{
    PagePlan Plan(ReportRevision revision, TemplateDefinition template, DoctorVersion? doctor);
    RenderedDocument Render(PagePlan plan, string format, DateTimeOffset timestamp);
    void ValidateImage(byte[]? png);
}
public interface IDocumentRepository
{
    Task<GeneratedDocumentInfo> Store(Guid revisionId, RenderedDocument document, Actor actor, CancellationToken ct);
    Task<StoredDocument> Get(Guid id, CancellationToken ct);
}
public interface IUserSessions
{
    Task<LoginResponse?> Login(LoginRequest request, CancellationToken ct);
    Task<Actor?> Authenticate(string token, CancellationToken ct);
    Task Logout(string token, CancellationToken ct);
    Task<Guid> CreateUser(CreateUserRequest request, Actor? creator, CancellationToken ct);
}
