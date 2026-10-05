using AkReporting.Application;
using AkReporting.Contracts;
using AkReporting.Domain;
using static AkReporting.Infrastructure.Database;

namespace AkReporting.Infrastructure;

public sealed class PostgresDocuments(Database db, TimeProvider clock) : IDocumentRepository
{
    public async Task<GeneratedDocumentInfo> Store(Guid revisionId, RenderedDocument document, Actor actor, CancellationToken ct)
    {
        if (document.Bytes.Length is 0 or > 52428800) throw new ValidationException("Document size exceeds its storage limit.");
        var info = new GeneratedDocumentInfo { Id = Guid.NewGuid(), RevisionId = revisionId, Format = document.Format,
            Sha256 = Hash(document.Bytes), PageCount = document.Plan.Pages.Count, EngineVersion = document.Plan.EngineVersion };
        await using var c = await db.Source.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await using var cmd = Command(c, tx,
            "INSERT INTO generated_document VALUES (@id,@revision,@format,@bytes,@hash,@pages,@engine,CAST(@plan AS jsonb),@actor,@time) ON CONFLICT(revision_id,format,engine_version,sha256) DO NOTHING RETURNING id",
            ("id", info.Id), ("revision", revisionId), ("format", info.Format), ("bytes", document.Bytes), ("hash", info.Sha256),
            ("pages", info.PageCount), ("engine", info.EngineVersion), ("plan", Json(document.Plan)), ("actor", actor.Id), ("time", clock.GetUtcNow().UtcDateTime));
        var created = await cmd.ExecuteScalarAsync(ct);
        if (created == null)
        {
            await using var find = Command(c, tx, "SELECT id FROM generated_document WHERE revision_id=@revision AND format=@format AND engine_version=@engine AND sha256=@hash",
                ("revision", revisionId), ("format", info.Format), ("engine", info.EngineVersion), ("hash", info.Sha256));
            info.Id = (Guid)(await find.ExecuteScalarAsync(ct))!;
        }
        await Audit(c, tx, actor, "document.generate", info.Id, null, ct);
        await tx.CommitAsync(ct);
        return info;
    }
    public async Task<StoredDocument> Get(Guid id, CancellationToken ct)
    {
        await using var c = await db.Source.OpenConnectionAsync(ct);
        await using var cmd = Command(c, null, "SELECT revision_id,format,bytes,sha256,page_count,engine_version FROM generated_document WHERE id=@id", ("id", id));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) throw new NotFoundException("Document not found.");
        var bytes = r.GetFieldValue<byte[]>(2);
        if (Hash(bytes) != r.GetString(3)) throw new IntegrityException("Stored document hash mismatch.");
        return new StoredDocument(new GeneratedDocumentInfo { Id = id, RevisionId = r.GetGuid(0), Format = r.GetString(1),
            Sha256 = r.GetString(3), PageCount = r.GetInt32(4), EngineVersion = r.GetString(5) }, bytes);
    }
}
