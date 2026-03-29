namespace Elysium.WorkStation.Services
{
    public static class DatabasePathProvider
    {
        private const string SqliteDbPathKey = "sqlite_db_path";
        private const string ReleaseDbFileName = "elysium.db";
        private const string DebugClientDbFileName = "elysium.debug.client.db";
        private const string DebugServerDbFileName = "elysium.debug.server.db";

        public static string DefaultPath => Path.Combine(FileSystem.AppDataDirectory, GetDefaultFileName());

        public static string GetPath()
        {
            var stored = ScopedPreferences.Get(SqliteDbPathKey, string.Empty);
            return NormalizeOrDefault(stored);
        }

        public static void SetPath(string path)
        {
            var current = GetPath();
            var next = NormalizeOrDefault(path);
            if (!string.Equals(current, next, StringComparison.OrdinalIgnoreCase))
            {
                CloseSqlitePools();
            }

            ScopedPreferences.Set(SqliteDbPathKey, next);
        }

        public static void ResetToDefault()
        {
            CloseSqlitePools();
            ScopedPreferences.Remove(SqliteDbPathKey);
        }

        public static string NormalizeOrDefault(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return DefaultPath;
            }

            try
            {
                var candidate = path.Trim().Trim('"');

                if (candidate.EndsWith(Path.DirectorySeparatorChar) ||
                    candidate.EndsWith(Path.AltDirectorySeparatorChar) ||
                    Directory.Exists(candidate))
                {
                    candidate = Path.Combine(candidate, GetDefaultFileName());
                }
                else if (!Path.HasExtension(candidate))
                {
                    candidate = candidate + ".db";
                }

                return Path.GetFullPath(candidate);
            }
            catch
            {
                return DefaultPath;
            }
        }

        private static void CloseSqlitePools()
        {
            try
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            }
            catch
            {
                // Ignore if provider is not initialized yet.
            }
        }

        private static string GetDefaultFileName()
        {
            return PreferenceScopeProvider.CurrentGroup switch
            {
                PreferenceGroup.DebugClient => DebugClientDbFileName,
                PreferenceGroup.DebugServer => DebugServerDbFileName,
                _ => ReleaseDbFileName
            };
        }
    }
}
