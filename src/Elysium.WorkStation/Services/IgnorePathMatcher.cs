using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace Elysium.WorkStation.Services
{
    public static class IgnorePathMatcher
    {
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
        private static readonly ConcurrentDictionary<string, Regex> RegexCache = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, Regex> WildcardCache = new(StringComparer.OrdinalIgnoreCase);

        public static string NormalizeEntry(string entry)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                return string.Empty;
            }

            var trimmed = entry.Trim();
            if (trimmed.StartsWith("regex:", StringComparison.OrdinalIgnoreCase))
            {
                var pattern = trimmed[6..].Trim();
                return string.IsNullOrWhiteSpace(pattern) ? string.Empty : $"regex:{pattern}";
            }

            return NormalizePathLike(trimmed);
        }

        public static string NormalizePathLike(string value)
        {
            return (value ?? string.Empty)
                .Trim()
                .Replace('\\', '/')
                .TrimStart('/');
        }

        public static bool IsPattern(string entry)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                return false;
            }

            var normalized = NormalizeEntry(entry);
            if (normalized.StartsWith("regex:", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return normalized.Contains('*') || normalized.Contains('?');
        }

        public static bool IsIgnored(string relativePath, IReadOnlyList<string> ignoreEntries)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || ignoreEntries is null || ignoreEntries.Count == 0)
            {
                return false;
            }

            var candidate = NormalizePathLike(relativePath);
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            foreach (var raw in ignoreEntries)
            {
                var ignore = NormalizeEntry(raw);
                if (string.IsNullOrWhiteSpace(ignore))
                {
                    continue;
                }

                if (ignore.StartsWith("regex:", StringComparison.OrdinalIgnoreCase))
                {
                    var pattern = ignore[6..];
                    if (IsRegexMatch(candidate, pattern))
                    {
                        return true;
                    }

                    continue;
                }

                if (ignore.Contains('*') || ignore.Contains('?'))
                {
                    if (IsWildcardMatch(candidate, ignore))
                    {
                        return true;
                    }

                    continue;
                }

                if (string.Equals(candidate, ignore, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (candidate.StartsWith(ignore + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsValidRegexEntry(string entry)
        {
            var normalized = NormalizeEntry(entry);
            if (!normalized.StartsWith("regex:", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            try
            {
                _ = GetRegex(normalized[6..]);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsRegexMatch(string candidate, string pattern)
        {
            try
            {
                var regex = GetRegex(pattern);
                return regex.IsMatch(candidate);
            }
            catch
            {
                return false;
            }
        }

        private static Regex GetRegex(string pattern)
        {
            return RegexCache.GetOrAdd(pattern, p =>
                new Regex(
                    p,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    RegexTimeout));
        }

        private static bool IsWildcardMatch(string candidate, string pattern)
        {
            var regex = WildcardCache.GetOrAdd(pattern, CreateWildcardRegex);
            return regex.IsMatch(candidate);
        }

        private static Regex CreateWildcardRegex(string pattern)
        {
            var normalized = NormalizePathLike(pattern);
            var sb = new StringBuilder("^");
            foreach (var ch in normalized)
            {
                _ = ch switch
                {
                    '*' => sb.Append(".*"),
                    '?' => sb.Append('.'),
                    _ => sb.Append(Regex.Escape(ch.ToString()))
                };
            }

            sb.Append('$');
            return new Regex(
                sb.ToString(),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                RegexTimeout);
        }
    }
}
