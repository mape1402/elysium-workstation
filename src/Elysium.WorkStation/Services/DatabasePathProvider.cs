namespace Elysium.WorkStation.Services
{
    public static class DatabasePathProvider
    {
        private const string SqliteDbPathKey = "sqlite_db_path";
        private const string DefaultDbFileName = "elysium.db";

        public static string DefaultPath => Path.Combine(FileSystem.AppDataDirectory, DefaultDbFileName);

        public static string GetPath()
        {
            var stored = Preferences.Default.Get(SqliteDbPathKey, string.Empty);
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

            Preferences.Default.Set(SqliteDbPathKey, next);
        }

        public static void ResetToDefault()
        {
            CloseSqlitePools();
            Preferences.Default.Remove(SqliteDbPathKey);
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
                    candidate = Path.Combine(candidate, DefaultDbFileName);
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
    }
}
