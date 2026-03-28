using Elysium.WorkStation.Data;
using Elysium.WorkStation.Models;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Data.Common;
using System.Globalization;

namespace Elysium.WorkStation.Services
{
    public class FolderSyncRepository : IFolderSyncRepository
    {
        private readonly IDbContextFactory<AppDbContext> _factory;

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
            await db.Database.ExecuteSqlRawAsync("""
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
                """);

            await db.Database.ExecuteSqlRawAsync("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_FolderSyncLinks_SyncId"
                ON "FolderSyncLinks" ("SyncId")
                """);

            var conn = db.Database.GetDbConnection();
            var shouldClose = conn.State != ConnectionState.Open;
            if (shouldClose)
            {
                await conn.OpenAsync();
            }

            try
            {
                await TryAddColumnAsync(conn, "SyncId", "TEXT NOT NULL DEFAULT ''");
                await TryAddColumnAsync(conn, "Name", "TEXT NOT NULL DEFAULT ''");
                await TryAddColumnAsync(conn, "Description", "TEXT NOT NULL DEFAULT ''");
                await TryAddColumnAsync(conn, "LocalFolderPath", "TEXT NOT NULL DEFAULT ''");
                await TryAddColumnAsync(conn, "IgnorePathsJson", "TEXT NOT NULL DEFAULT '[]'");
                await TryAddColumnAsync(conn, "LocalClientId", "TEXT NOT NULL DEFAULT ''");
                await TryAddColumnAsync(conn, "RemoteClientId", "TEXT NOT NULL DEFAULT ''");
                await TryAddColumnAsync(conn, "RemoteClientName", "TEXT NOT NULL DEFAULT ''");
                await TryAddColumnAsync(conn, "IsPendingOutgoing", "INTEGER NOT NULL DEFAULT 0");
                await TryAddColumnAsync(conn, "IsPendingIncoming", "INTEGER NOT NULL DEFAULT 0");
                await TryAddColumnAsync(conn, "IsAccepted", "INTEGER NOT NULL DEFAULT 0");
                await TryAddColumnAsync(conn, "ContinuousSyncEnabled", "INTEGER NOT NULL DEFAULT 0");
                await TryAddColumnAsync(conn, "IsEmitter", "INTEGER NOT NULL DEFAULT 0");
                await TryAddColumnAsync(conn, "LastSnapshotJson", "TEXT NOT NULL DEFAULT ''");
                await TryAddColumnAsync(conn, "LastStateHash", "TEXT NOT NULL DEFAULT ''");
                await TryAddColumnAsync(conn, "CreatedAt", "TEXT NOT NULL DEFAULT ''");
                await TryAddColumnAsync(conn, "UpdatedAt", "TEXT NOT NULL DEFAULT ''");
            }
            finally
            {
                if (shouldClose)
                {
                    await conn.CloseAsync();
                }
            }

        }

        private static async Task TryAddColumnAsync(System.Data.Common.DbConnection conn, string column, string definition)
        {
            await using var probe = conn.CreateCommand();
            probe.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('FolderSyncLinks') WHERE name = '{column}'";
            var exists = Convert.ToInt32(await probe.ExecuteScalarAsync()) > 0;
            if (exists)
            {
                return;
            }

            await using var alter = conn.CreateCommand();
            alter.CommandText = $"""ALTER TABLE "FolderSyncLinks" ADD COLUMN "{column}" {definition}""";
            await alter.ExecuteNonQueryAsync();
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
