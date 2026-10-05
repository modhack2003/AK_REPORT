using AkReporting.Application;
using AkReporting.Contracts;
using AkReporting.Domain;

namespace AkReporting.Api;

public static class ReportEndpoints
{
    public static void MapReportEndpoints(this WebApplication app)
    {
        app.MapGet("/cases", async (HttpContext context, IReportRepository reports, CancellationToken ct) =>
        {
            SessionMiddleware.RequireRole(context, "Writer", "MedicalReviewer", "Receptionist");
            return await reports.Cases(ct);
        });
        app.MapPost("/cases", async (CreateCaseRequest request, HttpContext context, IReportRepository reports, CancellationToken ct) =>
        {
            var actor = SessionMiddleware.RequireRole(context, "Writer", "MedicalReviewer", "Receptionist");
            return await reports.CreateCase(request, actor, ct);
        });
        app.MapPost("/cases/search", async (CaseSearchRequest request, HttpContext context, IReportRepository reports, CancellationToken ct) =>
        {
            SessionMiddleware.RequireRole(context, "Writer", "MedicalReviewer", "Receptionist");
            return await reports.SearchCases(request, ct);
        });
        app.MapGet("/cases/{caseId:guid}/reports", async (Guid caseId, HttpContext context, IReportRepository reports, CancellationToken ct) =>
        {
            SessionMiddleware.RequireRole(context, "Writer", "MedicalReviewer");
            return await reports.Reports(caseId, ct);
        });
        app.MapPost("/reports", async (CreateReportRequest request, HttpContext context, ReportService service, CancellationToken ct) =>
            await service.Create(request, SessionMiddleware.Actor(context), ct));
        app.MapGet("/reports/{id:guid}", async (Guid id, int? revision, HttpContext context, IReportRepository reports, CancellationToken ct) =>
        {
            SessionMiddleware.RequireRole(context, "Writer", "MedicalReviewer");
            return await reports.Revision(id, revision, ct);
        });
        app.MapGet("/reports/{id:guid}/history", async (Guid id, HttpContext context, IReportRepository reports, CancellationToken ct) =>
        {
            SessionMiddleware.RequireRole(context, "Writer", "MedicalReviewer");
            return await reports.History(id, ct);
        });
        app.MapPost("/reports/{id:guid}/revisions", async (Guid id, SaveRevisionRequest request, HttpContext context, ReportService service, CancellationToken ct) =>
            await service.Save(id, request, SessionMiddleware.Actor(context), ct));
        app.MapPost("/reports/{id:guid}/issue", async (Guid id, IssueReportRequest request, HttpContext context, ReportService service, CancellationToken ct) =>
            await service.Issue(id, request, SessionMiddleware.Actor(context), ct));
        app.MapGet("/reports/{id:guid}/preview", async (Guid id, int? revision, HttpContext context, ReportService service, CancellationToken ct) =>
            await service.Preview(id, revision, SessionMiddleware.Actor(context), ct));
        app.MapPost("/reports/{id:guid}/documents/{format}", async (Guid id, string format, int? revision, HttpContext context, ReportService service, CancellationToken ct) =>
            await service.Generate(id, revision, format, SessionMiddleware.Actor(context), ct));
        app.MapGet("/documents/{id:guid}", async (Guid id, HttpContext context, IDocumentRepository documents, CancellationToken ct) =>
        {
            SessionMiddleware.RequireRole(context, "Writer", "MedicalReviewer");
            var doc = await documents.Get(id, ct);
            return Results.File(doc.Bytes, doc.Info.Format == "pdf" ? "application/pdf" : "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                $"report-{id:N}.{doc.Info.Format}");
        });
        app.MapPost("/reports/{id:guid}/retention/{state}", async (Guid id, RetentionState state, HttpContext context, IReportRepository reports, CancellationToken ct) =>
        {
            var actor = SessionMiddleware.RequireRole(context, "Administrator");
            await reports.SetRetention(id, state, actor, ct);
            return Results.NoContent();
        });
    }
}
