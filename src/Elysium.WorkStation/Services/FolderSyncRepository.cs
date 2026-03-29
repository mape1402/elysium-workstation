using Elysium.WorkStation.Data;
using Elysium.WorkStation.Models;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;

namespace Elysium.WorkStation.Services
{
    public class FolderSyncRepository : IFolderSyncRepository
    {
        private readonly IDbContextFactory<AppDbContext> _factory;
        private static readonly SemaphoreSlim SchemaGate = new(1, 1);
        private static readonly HashSet<string> VerifiedDatabases = [];
        private static readonly string[] RequiredColumns =
        [
            "Id",
            "SyncId",
            "Name",
            "Description",
            "LocalFolderPath",
            "IgnorePathsJson",
            "LocalClientId",
            "RemoteClientId",
            "RemoteClientName",
            "IsPendingOutgoing",
            "IsPendingIncoming",
            "IsAccepted",
            "ContinuousSyncEnabled",
            "IsEmitter",
            "LastSnapshotJson",
            "LastStateHash",
            "CreatedAt",
            "UpdatedAt"
        ];

        public FolderSyncRepository(IDbContextFactory<AppDbContext> factory)
        {
            _factory = factory;
        }

        public async Task<List<FolderSyncLink>> GetAllAsync()
        {
            await using var db = await _factory.CreateDbContextAsync();
            await EnsureSchemaAsync(db);
            return await QueryLinksAsync(
                db,
                """
                SELECT
                    "Id",
                    "SyncId",
                    "Name",
                    "Description",
                    "LocalFolderPath",
                    "IgnorePathsJson",
                    "LocalClientId",
                    "RemoteClientId",
                    "RemoteClientName",
                    "IsPendingOutgoing",
                    "IsPendingIncoming",
                    "IsAccepted",
                    "ContinuousSyncEnabled",
                    "IsEmitter",
                    "LastSnapshotJson",
                    "LastStateHash",
                    "CreatedAt",
                    "UpdatedAt"
                FROM "FolderSyncLinks"
                ORDER BY "Name" ASC, "UpdatedAt" DESC
                """);
        }

        public async Task<FolderSyncLink> GetByIdAsync(int id)
        {
            await using var db = await _factory.CreateDbContextAsync();
            await EnsureSchemaAsync(db);
            var rows = await QueryLinksAsync(
                db,
                """
                SELECT
                    "Id",
                    "SyncId",
                    "Name",
                    "Description",
                    "LocalFolderPath",
                    "IgnorePathsJson",
                    "LocalClientId",
                    "RemoteClientId",
                    "RemoteClientName",
                    "IsPendingOutgoing",
                    "IsPendingIncoming",
                    "IsAccepted",
                    "ContinuousSyncEnabled",
                    "IsEmitter",
                    "LastSnapshotJson",
                    "LastStateHash",
                    "CreatedAt",
                    "UpdatedAt"
                FROM "FolderSyncLinks"
                WHERE "Id" = @id
                LIMIT 1
                """,
                cmd =>
                {
                    var p = cmd.CreateParameter();
                    p.ParameterName = "@id";
                    p.Value = id;
                    cmd.Parameters.Add(p);
                });
            return rows.FirstOrDefault();
        }

        public async Task<FolderSyncLink> GetBySyncIdAsync(string syncId)
        {
            await using var db = await _factory.CreateDbContextAsync();
            await EnsureSchemaAsync(db);
            var rows = await QueryLinksAsync(
                db,
                """
                SELECT
                    "Id",
                    "SyncId",
                    "Name",
                    "Description",
                    "LocalFolderPath",
                    "IgnorePathsJson",
                    "LocalClientId",
                    "RemoteClientId",
                    "RemoteClientName",
                    "IsPendingOutgoing",
                    "IsPendingIncoming",
                    "IsAccepted",
                    "ContinuousSyncEnabled",
                    "IsEmitter",
                    "LastSnapshotJson",
                    "LastStateHash",
                    "CreatedAt",
                    "UpdatedAt"
                FROM "FolderSyncLinks"
                WHERE "SyncId" = @syncId
                LIMIT 1
                """,
                cmd =>
                {
                    var p = cmd.CreateParameter();
                    p.ParameterName = "@syncId";
                    p.Value = syncId ?? string.Empty;
                    cmd.Parameters.Add(p);
                });
            return rows.FirstOrDefault();
        }

        public async Task<FolderSyncLink> SaveAsync(FolderSyncLink link)
        {
            await using var db = await _factory.CreateDbContextAsync();
            await EnsureSchemaAsync(db);

            link.UpdatedAt = DateTime.Now;
            if (link.Id == 0)
            {
                link.CreatedAt = DateTime.Now;
                db.FolderSyncLinks.Add(link);
            }
            else
            {
                db.FolderSyncLinks.Update(link);
            }

            await db.SaveChangesAsync();
            return link;
        }

        public async Task DeleteAsync(int id)
        {
            await using var db = await _factory.CreateDbContextAsync();
            await EnsureSchemaAsync(db);
            await db.FolderSyncLinks
                .Where(l => l.Id == id)
                .ExecuteDeleteAsync();
        }

        private static async Task EnsureSchemaAsync(AppDbContext db)
        {
            var key = GetSchemaCacheKey(db);
            lock (VerifiedDatabases)
            {
                if (VerifiedDatabases.Contains(key))
                {
                    return;
                }
            }

            await SchemaGate.WaitAsync();
            try
            {
                lock (VerifiedDatabases)
                {
                    if (VerifiedDatabases.Contains(key))
                    {
                        return;
                    }
                }

                var conn = db.Database.GetDbConnection();
                var shouldClose = conn.State != ConnectionState.Open;
                if (shouldClose)
                {
                    await conn.OpenAsync();
                }

                try
                {
                    await EnsureFolderSyncTableExistsAsync(conn);

                    var currentColumns = await GetColumnNamesAsync(conn, "FolderSyncLinks");
                    var hasAllColumns = RequiredColumns.All(currentColumns.Contains);
                    var hasUniqueSyncIndex = await HasUniqueSyncIdIndexAsync(conn);
                    var shouldRebuild = !hasAllColumns || !hasUniqueSyncIndex;

                    if (shouldRebuild)
                    {
                        await RebuildFolderSyncTableAsync(conn, currentColumns);
                    }
                    else
                    {
                        await EnsureSyncIdIndexAsync(conn);
                    }
                }
                finally
                {
                    if (shouldClose)
                    {
                        await conn.CloseAsync();
                    }
                }

                lock (VerifiedDatabases)
                {
                    VerifiedDatabases.Add(key);
                }
            }
            finally
            {
                SchemaGate.Release();
            }
        }

        private static string GetSchemaCacheKey(AppDbContext db)
        {
            try
            {
                var connectionString = db.Database.GetDbConnection().ConnectionString;
                if (!string.IsNullOrWhiteSpace(connectionString))
                {
                    return connectionString;
                }
            }
            catch
            {
                // ignored: fallback below
            }

            return "default";
        }

        private static async Task EnsureFolderSyncTableExistsAsync(DbConnection conn)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS "FolderSyncLinks" (
                    "Id"                    INTEGER NOT NULL CONSTRAINT "PK_FolderSyncLinks" PRIMARY KEY AUTOINCREMENT,
                    "SyncId"                TEXT    NOT NULL,
                    "Name"                  TEXT    NOT NULL,
                    "Description"           TEXT    NOT NULL DEFAULT '',
                    "LocalFolderPath"       TEXT    NOT NULL DEFAULT '',
                    "IgnorePathsJson"       TEXT    NOT NULL DEFAULT '[]',
                    "LocalClientId"         TEXT    NOT NULL DEFAULT '',
                    "RemoteClientId"        TEXT    NOT NULL DEFAULT '',
                    "RemoteClientName"      TEXT    NOT NULL DEFAULT '',
                    "IsPendingOutgoing"     INTEGER NOT NULL DEFAULT 0,
                    "IsPendingIncoming"     INTEGER NOT NULL DEFAULT 0,
                    "IsAccepted"            INTEGER NOT NULL DEFAULT 0,
                    "ContinuousSyncEnabled" INTEGER NOT NULL DEFAULT 0,
                    "IsEmitter"             INTEGER NOT NULL DEFAULT 0,
                    "LastSnapshotJson"      TEXT    NOT NULL DEFAULT '',
                    "LastStateHash"         TEXT    NOT NULL DEFAULT '',
                    "CreatedAt"             TEXT    NOT NULL DEFAULT '',
                    "UpdatedAt"             TEXT    NOT NULL DEFAULT ''
                )
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task<HashSet<string>> GetColumnNamesAsync(DbConnection conn, string tableName)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT name FROM pragma_table_info('{tableName}')";
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = Convert.ToString(reader.GetValue(0));
                if (!string.IsNullOrWhiteSpace(name))
                {
                    result.Add(name);
                }
            }

            return result;
        }

        private static async Task<bool> HasUniqueSyncIdIndexAsync(DbConnection conn)
        {
            var uniqueIndexNames = new List<string>();
            await using var indexList = conn.CreateCommand();
            indexList.CommandText = "PRAGMA index_list('FolderSyncLinks')";
            await using var reader = await indexList.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var isUnique = Convert.ToInt32(reader.GetValue(2)) == 1;
                var indexName = Convert.ToString(reader.GetValue(1)) ?? string.Empty;
                if (isUnique && !string.IsNullOrWhiteSpace(indexName))
                {
                    uniqueIndexNames.Add(indexName);
                }
            }

            foreach (var indexName in uniqueIndexNames)
            {
                await using var indexInfo = conn.CreateCommand();
                indexInfo.CommandText = $"PRAGMA index_info('{indexName.Replace("'", "''")}')";
                await using var cols = await indexInfo.ExecuteReaderAsync();

                var hasOnlySyncId = false;
                var columnCount = 0;
                while (await cols.ReadAsync())
                {
                    columnCount++;
                    var colName = Convert.ToString(cols.GetValue(2)) ?? string.Empty;
                    hasOnlySyncId = string.Equals(colName, "SyncId", StringComparison.OrdinalIgnoreCase);
                    if (!hasOnlySyncId)
                    {
                        break;
                    }
                }

                if (columnCount == 1 && hasOnlySyncId)
                {
                    return true;
                }
            }

            return false;
        }

        private static async Task RebuildFolderSyncTableAsync(DbConnection conn, HashSet<string> currentColumns)
        {
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                await ExecuteAsync(conn, tx, """DROP TABLE IF EXISTS "FolderSyncLinks_rebuild" """);
                await ExecuteAsync(conn, tx, """
                    CREATE TABLE "FolderSyncLinks_rebuild" (
                        "Id"                    INTEGER NOT NULL CONSTRAINT "PK_FolderSyncLinks" PRIMARY KEY AUTOINCREMENT,
                        "SyncId"                TEXT    NOT NULL,
                        "Name"                  TEXT    NOT NULL,
                        "Description"           TEXT    NOT NULL DEFAULT '',
                        "LocalFolderPath"       TEXT    NOT NULL DEFAULT '',
                        "IgnorePathsJson"       TEXT    NOT NULL DEFAULT '[]',
                        "LocalClientId"         TEXT    NOT NULL DEFAULT '',
                        "RemoteClientId"        TEXT    NOT NULL DEFAULT '',
                        "RemoteClientName"      TEXT    NOT NULL DEFAULT '',
                        "IsPendingOutgoing"     INTEGER NOT NULL DEFAULT 0,
                        "IsPendingIncoming"     INTEGER NOT NULL DEFAULT 0,
                        "IsAccepted"            INTEGER NOT NULL DEFAULT 0,
                        "ContinuousSyncEnabled" INTEGER NOT NULL DEFAULT 0,
                        "IsEmitter"             INTEGER NOT NULL DEFAULT 0,
                        "LastSnapshotJson"      TEXT    NOT NULL DEFAULT '',
                        "LastStateHash"         TEXT    NOT NULL DEFAULT '',
                        "CreatedAt"             TEXT    NOT NULL DEFAULT '',
                        "UpdatedAt"             TEXT    NOT NULL DEFAULT ''
                    )
                    """);

                if (await TableExistsAsync(conn, tx, "FolderSyncLinks"))
                {
                    var insertSql = BuildCopySql(currentColumns);
                    await ExecuteAsync(conn, tx, insertSql);
                }

                await ExecuteAsync(conn, tx, """
                    UPDATE "FolderSyncLinks_rebuild"
                    SET "SyncId" = lower(hex(randomblob(16)))
                    WHERE "SyncId" IS NULL OR trim("SyncId") = ''
                    """);

                await ExecuteAsync(conn, tx, """
                    WITH duplicate_rows AS (
                        SELECT
                            "Id",
                            ROW_NUMBER() OVER (PARTITION BY "SyncId" ORDER BY "Id") AS rn
                        FROM "FolderSyncLinks_rebuild"
                    )
                    UPDATE "FolderSyncLinks_rebuild"
                    SET "SyncId" = lower(hex(randomblob(16)))
                    WHERE "Id" IN (SELECT "Id" FROM duplicate_rows WHERE rn > 1)
                    """);

                await ExecuteAsync(conn, tx, """DROP TABLE IF EXISTS "FolderSyncLinks" """);
                await ExecuteAsync(conn, tx, """ALTER TABLE "FolderSyncLinks_rebuild" RENAME TO "FolderSyncLinks" """);
                await EnsureSyncIdIndexAsync(conn, tx);
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        private static string BuildCopySql(HashSet<string> currentColumns)
        {
            string Text(string col, string fallback) =>
                currentColumns.Contains(col)
                    ? $"""COALESCE(NULLIF(CAST("{col}" AS TEXT), ''), '{fallback}')"""
                    : $"'{fallback}'";
            string Num(string col, string fallback) =>
                currentColumns.Contains(col)
                    ? $"""COALESCE(CAST("{col}" AS INTEGER), {fallback})"""
                    : fallback;
            string Timestamp(string col) =>
                currentColumns.Contains(col)
                    ? $"""COALESCE(NULLIF(CAST("{col}" AS TEXT), ''), strftime('%Y-%m-%dT%H:%M:%fZ','now'))"""
                    : """strftime('%Y-%m-%dT%H:%M:%fZ','now')""";

            var select = new StringBuilder();
            select.AppendLine("INSERT INTO \"FolderSyncLinks_rebuild\" (");
            select.AppendLine("    \"SyncId\",");
            select.AppendLine("    \"Name\",");
            select.AppendLine("    \"Description\",");
            select.AppendLine("    \"LocalFolderPath\",");
            select.AppendLine("    \"IgnorePathsJson\",");
            select.AppendLine("    \"LocalClientId\",");
            select.AppendLine("    \"RemoteClientId\",");
            select.AppendLine("    \"RemoteClientName\",");
            select.AppendLine("    \"IsPendingOutgoing\",");
            select.AppendLine("    \"IsPendingIncoming\",");
            select.AppendLine("    \"IsAccepted\",");
            select.AppendLine("    \"ContinuousSyncEnabled\",");
            select.AppendLine("    \"IsEmitter\",");
            select.AppendLine("    \"LastSnapshotJson\",");
            select.AppendLine("    \"LastStateHash\",");
            select.AppendLine("    \"CreatedAt\",");
            select.AppendLine("    \"UpdatedAt\"");
            select.AppendLine(")");
            select.AppendLine("SELECT");
            var syncIdExpr = currentColumns.Contains("SyncId")
                ? """COALESCE(NULLIF(CAST("SyncId" AS TEXT), ''), lower(hex(randomblob(16))))"""
                : """lower(hex(randomblob(16)))""";
            select.AppendLine($"""    {syncIdExpr},""");
            select.AppendLine($"""    {Text("Name", "Sin nombre")},""");
            select.AppendLine($"""    {Text("Description", string.Empty)},""");
            select.AppendLine($"""    {Text("LocalFolderPath", string.Empty)},""");
            select.AppendLine($"""    {Text("IgnorePathsJson", "[]")},""");
            select.AppendLine($"""    {Text("LocalClientId", string.Empty)},""");
            select.AppendLine($"""    {Text("RemoteClientId", string.Empty)},""");
            select.AppendLine($"""    {Text("RemoteClientName", string.Empty)},""");
            select.AppendLine($"""    {Num("IsPendingOutgoing", "0")},""");
            select.AppendLine($"""    {Num("IsPendingIncoming", "0")},""");
            select.AppendLine($"""    {Num("IsAccepted", "0")},""");
            select.AppendLine($"""    {Num("ContinuousSyncEnabled", "0")},""");
            select.AppendLine($"""    {Num("IsEmitter", "0")},""");
            select.AppendLine($"""    {Text("LastSnapshotJson", string.Empty)},""");
            select.AppendLine($"""    {Text("LastStateHash", string.Empty)},""");
            select.AppendLine($"""    {Timestamp("CreatedAt")},""");
            select.AppendLine($"""    {Timestamp("UpdatedAt")}""");
            select.AppendLine("FROM \"FolderSyncLinks\"");
            return select.ToString();
        }

        private static async Task<bool> TableExistsAsync(DbConnection conn, DbTransaction tx, string tableName)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name";
            var p = cmd.CreateParameter();
            p.ParameterName = "@name";
            p.Value = tableName;
            cmd.Parameters.Add(p);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }

        private static Task EnsureSyncIdIndexAsync(DbConnection conn, DbTransaction tx = null) =>
            ExecuteAsync(
                conn,
                tx,
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_FolderSyncLinks_SyncId"
                ON "FolderSyncLinks" ("SyncId")
                """);

        private static async Task ExecuteAsync(DbConnection conn, DbTransaction tx, string sql)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task<List<FolderSyncLink>> QueryLinksAsync(
            AppDbContext db,
            string sql,
            Action<DbCommand> configure = null)
        {
            var conn = db.Database.GetDbConnection();
            var shouldClose = conn.State != ConnectionState.Open;
            if (shouldClose)
            {
                await conn.OpenAsync();
            }

            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                configure?.Invoke(cmd);

                var results = new List<FolderSyncLink>();
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    results.Add(MapLink(reader));
                }

                return results;
            }
            finally
            {
                if (shouldClose)
                {
                    await conn.CloseAsync();
                }
            }
        }

        private static FolderSyncLink MapLink(DbDataReader reader)
        {
            return new FolderSyncLink
            {
                Id = ReadInt(reader, "Id"),
                SyncId = ReadString(reader, "SyncId"),
                Name = ReadString(reader, "Name"),
                Description = ReadString(reader, "Description"),
                LocalFolderPath = ReadString(reader, "LocalFolderPath"),
                IgnorePathsJson = ReadString(reader, "IgnorePathsJson", "[]"),
                LocalClientId = ReadString(reader, "LocalClientId"),
                RemoteClientId = ReadString(reader, "RemoteClientId"),
                RemoteClientName = ReadString(reader, "RemoteClientName"),
                IsPendingOutgoing = ReadBool(reader, "IsPendingOutgoing"),
                IsPendingIncoming = ReadBool(reader, "IsPendingIncoming"),
                IsAccepted = ReadBool(reader, "IsAccepted"),
                ContinuousSyncEnabled = ReadBool(reader, "ContinuousSyncEnabled"),
                IsEmitter = ReadBool(reader, "IsEmitter"),
                LastSnapshotJson = ReadString(reader, "LastSnapshotJson"),
                LastStateHash = ReadString(reader, "LastStateHash"),
                CreatedAt = ReadDateTime(reader, "CreatedAt"),
                UpdatedAt = ReadDateTime(reader, "UpdatedAt")
            };
        }

        private static string ReadString(DbDataReader reader, string column, string fallback = "")
        {
            var ordinal = reader.GetOrdinal(column);
            if (reader.IsDBNull(ordinal))
            {
                return fallback;
            }

            return Convert.ToString(reader.GetValue(ordinal)) ?? fallback;
        }

        private static int ReadInt(DbDataReader reader, string column)
        {
            var ordinal = reader.GetOrdinal(column);
            if (reader.IsDBNull(ordinal))
            {
                return 0;
            }

            return Convert.ToInt32(reader.GetValue(ordinal));
        }

        private static bool ReadBool(DbDataReader reader, string column)
        {
            var ordinal = reader.GetOrdinal(column);
            if (reader.IsDBNull(ordinal))
            {
                return false;
            }

            var value = reader.GetValue(ordinal);
            return value switch
            {
                bool b => b,
                byte bt => bt != 0,
                short s => s != 0,
                int i => i != 0,
                long l => l != 0L,
                string s => string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) || s == "1",
                _ => Convert.ToInt32(value) != 0
            };
        }

        private static DateTime ReadDateTime(DbDataReader reader, string column)
        {
            var raw = ReadString(reader, column, string.Empty);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return DateTime.Now;
            }

            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var value))
            {
                return value;
            }

            if (DateTime.TryParse(raw, out value))
            {
                return value;
            }

            return DateTime.Now;
        }
    }
}
