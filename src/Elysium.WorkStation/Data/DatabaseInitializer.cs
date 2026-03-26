using Microsoft.EntityFrameworkCore;
using System.Data;

namespace Elysium.WorkStation.Data
{
    /// <summary>
    /// Handles schema creation and incremental migrations for the local SQLite database.
    /// Each table uses CREATE TABLE IF NOT EXISTS, so this is safe to run on both new
    /// installs and existing databases without data loss — except where a breaking schema
    /// change requires a table rebuild (noted inline).
    /// </summary>
    public static class DatabaseInitializer
    {
        public static void Initialize(AppDbContext db)
        {
            // Creates the DB file on first run; no-op if it already exists.
            db.Database.EnsureCreated();

            EnsureNotificationsTable(db);
            EnsureClipboardHistoryTable(db);
            EnsureFileHistoryTable(db);
            EnsureNotesTable(db);
            EnsureKanbanTasksTable(db);
            EnsureVariableGroupsTable(db);
            EnsureWorkVariablesTable(db);
        }

        private static void EnsureNotificationsTable(AppDbContext db) =>
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE IF NOT EXISTS "Notifications" (
                    "Id"        INTEGER NOT NULL CONSTRAINT "PK_Notifications" PRIMARY KEY AUTOINCREMENT,
                    "Title"     TEXT    NOT NULL,
                    "Message"   TEXT    NOT NULL,
                    "Timestamp" TEXT    NOT NULL,
                    "IsRead"    INTEGER NOT NULL
                )
                """);

        private static void EnsureClipboardHistoryTable(AppDbContext db) =>
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE IF NOT EXISTS "ClipboardHistory" (
                    "Id"         INTEGER NOT NULL CONSTRAINT "PK_ClipboardHistory" PRIMARY KEY AUTOINCREMENT,
                    "Text"       TEXT    NOT NULL,
                    "Timestamp"  TEXT    NOT NULL,
                    "SenderName" TEXT    NOT NULL,
                    "IsFromSelf" INTEGER NOT NULL
                )
                """);

        private static void EnsureFileHistoryTable(AppDbContext db)
        {
            // Schema v1 used FileId (TEXT) as the primary key, which prevents sending
            // the same logical file more than once per DB row. Schema v2 introduces a
            // proper AUTOINCREMENT surrogate PK and a UNIQUE index on FileId.
            // If the old schema is detected we drop and rebuild (file history is
            // non-critical metadata; a clean slate is acceptable on upgrade).
            MigrateFileHistoryIfNeeded(db);

            db.Database.ExecuteSqlRaw("""
                CREATE TABLE IF NOT EXISTS "FileHistory" (
                    "Id"         INTEGER NOT NULL CONSTRAINT "PK_FileHistory" PRIMARY KEY AUTOINCREMENT,
                    "FileId"     TEXT    NOT NULL UNIQUE,
                    "FileName"   TEXT    NOT NULL,
                    "FileSize"   INTEGER NOT NULL,
                    "SenderName" TEXT    NOT NULL,
                    "IsFromSelf" INTEGER NOT NULL,
                    "Timestamp"  TEXT    NOT NULL,
                    "SourcePath" TEXT
                )
                """);

            AddSourcePathColumnIfMissing(db);
        }

        /// <summary>
        /// Drops the FileHistory table when it still carries the old schema
        /// (no "Id" column). The subsequent CREATE TABLE IF NOT EXISTS will then
        /// build it fresh with the correct layout.
        /// </summary>
        private static void MigrateFileHistoryIfNeeded(AppDbContext db)
        {
            var conn = db.Database.GetDbConnection();
            bool shouldClose = conn.State != ConnectionState.Open;
            if (shouldClose) conn.Open();

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('FileHistory') WHERE name = 'Id'";
                bool hasNewSchema = Convert.ToInt32(cmd.ExecuteScalar()) > 0;

                if (!hasNewSchema)
                {
                    using var drop = conn.CreateCommand();
                    drop.CommandText = """DROP TABLE IF EXISTS "FileHistory" """;
                    drop.ExecuteNonQuery();
                }
            }
            finally
            {
                if (shouldClose) conn.Close();
            }
        }

        /// <summary>
        /// Adds the SourcePath column to existing FileHistory tables that were
        /// created before this column existed. ALTER TABLE ADD COLUMN is a no-op
        /// if the column is already present (SQLite returns an error we catch).
        /// </summary>
        private static void AddSourcePathColumnIfMissing(AppDbContext db)
        {
            var conn = db.Database.GetDbConnection();
            bool shouldClose = conn.State != ConnectionState.Open;
            if (shouldClose) conn.Open();

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('FileHistory') WHERE name = 'SourcePath'";
                bool hasColumn = Convert.ToInt32(cmd.ExecuteScalar()) > 0;

                if (!hasColumn)
                {
                    using var alter = conn.CreateCommand();
                    alter.CommandText = """ALTER TABLE "FileHistory" ADD COLUMN "SourcePath" TEXT""";
                    alter.ExecuteNonQuery();
                }
            }
            finally
            {
                if (shouldClose) conn.Close();
            }
        }

        private static void EnsureNotesTable(AppDbContext db)
        {
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE IF NOT EXISTS "Notes" (
                    "Id"        INTEGER NOT NULL CONSTRAINT "PK_Notes" PRIMARY KEY AUTOINCREMENT,
                    "Title"     TEXT    NOT NULL,
                    "Text"      TEXT    NOT NULL,
                    "ColorHex"  TEXT    NOT NULL DEFAULT '#FFF9C4',
                    "Timestamp" TEXT    NOT NULL
                )
                """);

            AddNotesColorColumnIfMissing(db);
        }

        private static void AddNotesColorColumnIfMissing(AppDbContext db)
        {
            var conn = db.Database.GetDbConnection();
            bool shouldClose = conn.State != ConnectionState.Open;
            if (shouldClose) conn.Open();

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Notes') WHERE name = 'ColorHex'";
                bool hasColumn = Convert.ToInt32(cmd.ExecuteScalar()) > 0;

                if (!hasColumn)
                {
                    using var alter = conn.CreateCommand();
                    alter.CommandText = """ALTER TABLE "Notes" ADD COLUMN "ColorHex" TEXT NOT NULL DEFAULT '#FFF9C4'""";
                    alter.ExecuteNonQuery();
                }
            }
            finally
            {
                if (shouldClose) conn.Close();
            }
        }

        private static void EnsureKanbanTasksTable(AppDbContext db)
        {
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE IF NOT EXISTS "KanbanTasks" (
                    "Id"          INTEGER NOT NULL CONSTRAINT "PK_KanbanTasks" PRIMARY KEY AUTOINCREMENT,
                    "Title"       TEXT    NOT NULL,
                    "Description" TEXT    NOT NULL DEFAULT '',
                    "Status"      INTEGER NOT NULL DEFAULT 0,
                    "Priority"    INTEGER NOT NULL DEFAULT 1,
                    "Visible"     INTEGER NOT NULL DEFAULT 1,
                    "CompletedOn" TEXT,
                    "SortOrder"   INTEGER NOT NULL DEFAULT 0,
                    "CreatedAt"   TEXT    NOT NULL
                )
                """);

            AddKanbanColumnIfMissing(db, "Priority",    "INTEGER NOT NULL DEFAULT 1");
            AddKanbanColumnIfMissing(db, "Visible",     "INTEGER NOT NULL DEFAULT 1");
            AddKanbanColumnIfMissing(db, "CompletedOn", "TEXT");
        }

        private static void AddKanbanColumnIfMissing(AppDbContext db, string column, string definition)
        {
            var conn = db.Database.GetDbConnection();
            bool shouldClose = conn.State != System.Data.ConnectionState.Open;
            if (shouldClose) conn.Open();

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('KanbanTasks') WHERE name = '{column}'";
                bool hasColumn = Convert.ToInt32(cmd.ExecuteScalar()) > 0;

                if (!hasColumn)
                {
                    using var alter = conn.CreateCommand();
                    alter.CommandText = $"""ALTER TABLE "KanbanTasks" ADD COLUMN "{column}" {definition}""";
                    alter.ExecuteNonQuery();
                }
            }
            finally
            {
                if (shouldClose) conn.Close();
            }
        }

        private static void EnsureVariableGroupsTable(AppDbContext db)
        {
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE IF NOT EXISTS "VariableGroups" (
                    "Id"          INTEGER NOT NULL CONSTRAINT "PK_VariableGroups" PRIMARY KEY AUTOINCREMENT,
                    "Name"        TEXT    NOT NULL,
                    "Description" TEXT    NOT NULL DEFAULT '',
                    "CreatedAt"   TEXT    NOT NULL
                )
                """);

            AddVariableGroupsColumnIfMissing(db, "Description", "TEXT NOT NULL DEFAULT ''");
            AddVariableGroupsColumnIfMissing(db, "CreatedAt", "TEXT NOT NULL DEFAULT ''");
        }

        private static void AddVariableGroupsColumnIfMissing(AppDbContext db, string column, string definition)
        {
            var conn = db.Database.GetDbConnection();
            bool shouldClose = conn.State != ConnectionState.Open;
            if (shouldClose) conn.Open();

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('VariableGroups') WHERE name = '{column}'";
                bool hasColumn = Convert.ToInt32(cmd.ExecuteScalar()) > 0;

                if (!hasColumn)
                {
                    using var alter = conn.CreateCommand();
                    alter.CommandText = $"""ALTER TABLE "VariableGroups" ADD COLUMN "{column}" {definition}""";
                    alter.ExecuteNonQuery();
                }
            }
            finally
            {
                if (shouldClose) conn.Close();
            }
        }

        private static void EnsureWorkVariablesTable(AppDbContext db)
        {
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE IF NOT EXISTS "WorkVariables" (
                    "Id"             INTEGER NOT NULL CONSTRAINT "PK_WorkVariables" PRIMARY KEY AUTOINCREMENT,
                    "GroupId"        INTEGER NOT NULL,
                    "VariableKey"    TEXT    NOT NULL,
                    "Value"          TEXT    NOT NULL DEFAULT '',
                    "Description"    TEXT    NOT NULL DEFAULT '',
                    "IsSecret"       INTEGER NOT NULL DEFAULT 0,
                    "EncryptedValue" TEXT    NOT NULL DEFAULT '',
                    "UpdatedAt"      TEXT    NOT NULL,
                    CONSTRAINT "FK_WorkVariables_VariableGroups_GroupId"
                        FOREIGN KEY ("GroupId") REFERENCES "VariableGroups" ("Id") ON DELETE CASCADE
                )
                """);

            db.Database.ExecuteSqlRaw("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_WorkVariables_GroupId_VariableKey"
                ON "WorkVariables" ("GroupId", "VariableKey")
                """);

            AddWorkVariablesColumnIfMissing(db, "Description", "TEXT NOT NULL DEFAULT ''");
            AddWorkVariablesColumnIfMissing(db, "IsSecret", "INTEGER NOT NULL DEFAULT 0");
            AddWorkVariablesColumnIfMissing(db, "EncryptedValue", "TEXT NOT NULL DEFAULT ''");
            AddWorkVariablesColumnIfMissing(db, "UpdatedAt", "TEXT NOT NULL DEFAULT ''");
        }

        private static void AddWorkVariablesColumnIfMissing(AppDbContext db, string column, string definition)
        {
            var conn = db.Database.GetDbConnection();
            bool shouldClose = conn.State != ConnectionState.Open;
            if (shouldClose) conn.Open();

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('WorkVariables') WHERE name = '{column}'";
                bool hasColumn = Convert.ToInt32(cmd.ExecuteScalar()) > 0;

                if (!hasColumn)
                {
                    using var alter = conn.CreateCommand();
                    alter.CommandText = $"""ALTER TABLE "WorkVariables" ADD COLUMN "{column}" {definition}""";
                    alter.ExecuteNonQuery();
                }
            }
            finally
            {
                if (shouldClose) conn.Close();
            }
        }
    }
}
