using AkReporting.Application;
using AkReporting.Contracts;
using AkReporting.Domain;

namespace AkReporting.Api;

public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this WebApplication app)
    {
        app.MapGet("/templates", async (HttpContext context, ICatalogRepository catalog, CancellationToken ct) =>
        {
            SessionMiddleware.RequireRole(context, "Administrator", "Writer", "MedicalReviewer");
            return await catalog.Templates(ct);
        });
        app.MapPost("/templates", async (TemplateDefinition definition, HttpContext context, ICatalogRepository catalog, CancellationToken ct) =>
        {
            var actor = SessionMiddleware.RequireRole(context, "Administrator");
            if (definition.ReviewStatus != ReviewStatus.Draft) throw new ValidationException("Import a draft; clinical approval is a separate reviewer action.");
            definition.ReviewedAt = null; definition.ReviewedBy = ""; definition.ReviewEvidence = "";
            return await catalog.AddTemplate(definition, actor, ct);
        });
        app.MapPost("/templates/seed-candidates", async (HttpContext context, ICatalogRepository catalog, CancellationToken ct) =>
        {
            var actor = SessionMiddleware.RequireRole(context, "Administrator");
            var existing = await catalog.Templates(ct);
            foreach (var t in CandidateTemplates.All())
                if (!existing.Any(e => e.ReportTypeCode == t.ReportTypeCode)) await catalog.AddTemplate(t, actor, ct);
            return Results.NoContent();
        });
        app.MapPost("/templates/{id:guid}/approve", async (Guid id, ApprovalRequest request, HttpContext context, ICatalogRepository catalog, TimeProvider clock, CancellationToken ct) =>
        {
            var actor = SessionMiddleware.RequireRole(context, "MedicalReviewer");
            ReportValidator.Required(request.ReviewerName, "Qualified reviewer identity", 200);
            ReportValidator.Required(request.Evidence, "Center/SOP/medical review evidence", 8000);
            var t = await catalog.Template(id, ct);
            if (t.ReviewStatus != ReviewStatus.Draft) throw new ConflictException("Template is already approved.");
            t.VersionId = Guid.NewGuid();
            t.Version = (await catalog.Templates(ct)).Where(v => v.ReportTypeCode == t.ReportTypeCode).Max(v => v.Version) + 1;
            t.ReviewStatus = ReviewStatus.Approved;
            t.ReviewedBy = request.ReviewerName; t.ReviewEvidence = request.Evidence; t.ReviewedAt = clock.GetUtcNow();
            return await catalog.AddTemplate(t, actor, ct);
        });
        app.MapGet("/doctors", async (HttpContext context, ICatalogRepository catalog, CancellationToken ct) =>
        {
            SessionMiddleware.RequireRole(context, "Administrator", "Writer", "MedicalReviewer");
            return await catalog.Doctors(ct); // No raw signature/stamp assets in the list.
        });
        app.MapPost("/doctors/versions", async (DoctorVersion doctor, HttpContext context, ICatalogRepository catalog, IReportRenderer renderer, CancellationToken ct) =>
        {
            var actor = SessionMiddleware.RequireRole(context, "Administrator");
            ReportValidator.Doctor(doctor);
            renderer.ValidateImage(doctor.SignaturePng); renderer.ValidateImage(doctor.StampPng);
            return await catalog.AddDoctorVersion(doctor, actor, ct);
        });
        app.MapPost("/doctors/{id:guid}/active/{active:bool}", async (Guid id, bool active, HttpContext context, ICatalogRepository catalog, CancellationToken ct) =>
        {
            var actor = SessionMiddleware.RequireRole(context, "Administrator");
            await catalog.SetDoctorActive(id, active, actor, ct);
            return Results.NoContent();
        });
        app.MapPost("/users", async (CreateUserRequest request, HttpContext context, IUserSessions sessions, CancellationToken ct) =>
            new { id = await sessions.CreateUser(request, SessionMiddleware.RequireRole(context, "Administrator"), ct) });
        app.MapGet("/settings", async (ICatalogRepository catalog, CancellationToken ct) => await catalog.Settings(ct));
        app.MapPost("/settings", async (CenterSettings settings, HttpContext context, ICatalogRepository catalog, CancellationToken ct) =>
        {
            await catalog.SetSettings(settings, SessionMiddleware.RequireRole(context, "Administrator"), ct);
            return Results.NoContent();
        });
    }
}
