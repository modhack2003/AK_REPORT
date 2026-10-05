# Controlled PostgreSQL backup and restore

No automatic or cloud backup is implemented. A trained administrator uses PostgreSQL tools; credentials are supplied through a protected password file/prompt, not command-line password literals.

## Export

Stop clinical editing/issuance for the controlled backup window and record the application/migration/renderer versions. A normal PostgreSQL dump gives a consistent database snapshot, including protected signature/document bytes.

```powershell
pg_dump --format=custom --file="D:\ProtectedBackups\ak-report.backup" --dbname=ak_reporting
pg_restore --list "D:\ProtectedBackups\ak-report.backup"
```

Protect the backup folder with service/administrator NTFS ACLs and suitable local encryption. Treat the backup as patient data. Record its file hash, timestamp and tool/server version in the protected operator log. Never commit it or upload it to the code repository.

## Restore drill

Restore into a **new empty database**, not over active reporting records:

```powershell
createdb ak_reporting_restore_check
pg_restore --exit-on-error --no-owner --no-privileges --dbname=ak_reporting_restore_check "D:\ProtectedBackups\ak-report.backup"
psql -d ak_reporting_restore_check -v runtime_role=ak_reporting_app -f database/grant-runtime.sql
```

Owner/role identities are installation-specific; review `--no-owner`/privilege settings and assign the correct database owner. Reapply runtime grants. Using an appropriate administrator, verify migration hashes, report/revision counts, version pins and representative document hashes. Launch an isolated host against the restored database and review historic artifacts. Do not let production clients access the drill database.

For actual recovery, stop the host, preserve the damaged database for investigation, restore a new database, verify it, then explicitly switch the protected host connection. Coordinate downtime and reconcile any reports issued after the selected backup. Never silently merge/overwrite revisions. No restore API is exposed to writers.

Application restart/transaction integrity is tested automatically. A center-specific backup/restore and power-loss drill is still mandatory release evidence.
