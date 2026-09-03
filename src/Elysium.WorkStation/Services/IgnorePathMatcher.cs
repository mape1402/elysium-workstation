using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace Elysium.WorkStation.Services
{
    public static class IgnorePathMatcher
    {
        private const string GitIgnorePrefix = "gitignore:";
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
        private static readonly ConcurrentDictionary<string, Regex> RegexCache = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, Regex> WildcardCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, Regex> GitIgnoreCache = new(StringComparer.OrdinalIgnoreCase);

        public static string CreateGitIgnoreEntry(string pattern)
        {
            var normalized = NormalizeGitIgnorePattern(pattern);
            return string.IsNullOrWhiteSpace(normalized) ? string.Empty : $"{GitIgnorePrefix}{normalized}";
        }

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

            return HasGitIgnorePatternTokens(normalized);
        }

        public static bool IsIgnored(string relativePath, IReadOnlyList<string> ignoreEntries)
        {
            return IsIgnored(relativePath, ignoreEntries, isDirectory: false);
        }

        public static bool IsIgnored(string relativePath, IReadOnlyList<string> ignoreEntries, bool isDirectory)
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

            var gitIgnored = false;
            foreach (var raw in ignoreEntries)
            {
                var ignore = NormalizeEntry(raw);
                if (string.IsNullOrWhiteSpace(ignore))
                {
                    continue;
                }

                if (ignore.StartsWith(GitIgnorePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    if (TryMatchGitIgnore(candidate, ignore[GitIgnorePrefix.Length..], isDirectory, out var ignored))
                    {
                        gitIgnored = ignored;
                    }

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

            return gitIgnored;
        }

        private static string NormalizeGitIgnorePattern(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                return string.Empty;
            }

            var trimmed = pattern.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            var negated = trimmed.StartsWith("!", StringComparison.Ordinal);
            if (negated)
            {
                trimmed = trimmed[1..].Trim();
            }

            if (trimmed.Length == 0)
            {
                return string.Empty;
            }

            trimmed = trimmed
                .Replace("\\ ", " ", StringComparison.Ordinal)
                .Replace("\\#", "#", StringComparison.Ordinal)
                .Replace("\\!", "!", StringComparison.Ordinal);
            trimmed = trimmed.Replace('\\', '/');
            return negated ? $"!{trimmed}" : trimmed;
        }

        private static bool TryMatchGitIgnore(string candidate, string rawPattern, bool isDirectory, out bool ignored)
        {
            ignored = false;
            var pattern = NormalizeGitIgnorePattern(rawPattern);
            if (string.IsNullOrWhiteSpace(pattern))
            {
                return false;
            }

            var negated = pattern.StartsWith("!", StringComparison.Ordinal);
            if (negated)
            {
                pattern = pattern[1..];
            }

            var rootAnchored = pattern.StartsWith("/", StringComparison.Ordinal);
            var directoryOnly = pattern.EndsWith("/", StringComparison.Ordinal);
            pattern = pattern.TrimStart('/').TrimEnd('/');
            if (string.IsNullOrWhiteSpace(pattern))
            {
                return false;
            }

            if (!IsGitIgnoreMatch(candidate, pattern, rootAnchored, directoryOnly, isDirectory))
            {
                return false;
            }

            ignored = !negated;
            return true;
        }

        private static bool IsGitIgnoreMatch(string candidate, string pattern, bool rootAnchored, bool directoryOnly, bool isDirectory)
        {
            var anchored = rootAnchored || pattern.Contains('/');
            if (anchored)
            {
                return IsGitIgnorePathMatch(candidate, pattern, directoryOnly, isDirectory);
            }

            var segments = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var segmentCount = directoryOnly && !isDirectory
                ? Math.Max(segments.Length - 1, 0)
                : segments.Length;

            for (var index = 0; index < segmentCount; index++)
            {
                if (IsGitIgnoreNameMatch(segments[index], pattern))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsGitIgnorePathMatch(string candidate, string pattern, bool directoryOnly, bool isDirectory)
        {
            if ((!directoryOnly || isDirectory) && IsGitIgnoreNameMatch(candidate, pattern))
            {
                return true;
            }

            foreach (var parentPath in EnumerateParentPaths(candidate))
            {
                if (IsGitIgnoreNameMatch(parentPath, pattern))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsGitIgnoreNameMatch(string candidate, string pattern)
        {
            if (!HasGitIgnorePatternTokens(pattern))
            {
                return string.Equals(candidate, pattern, StringComparison.OrdinalIgnoreCase);
            }

            var regex = GitIgnoreCache.GetOrAdd(pattern, CreateGitIgnoreRegex);
            return regex.IsMatch(candidate);
        }

        private static bool HasGitIgnorePatternTokens(string pattern)
        {
            return pattern.Contains('*') ||
                   pattern.Contains('?') ||
                   pattern.Contains('[');
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

        private static Regex CreateGitIgnoreRegex(string pattern)
        {
            var normalized = NormalizePathLike(pattern);
            var sb = new StringBuilder("^");
            for (var index = 0; index < normalized.Length; index++)
            {
                var ch = normalized[index];
                if (ch == '*')
                {
                    if (index + 1 < normalized.Length && normalized[index + 1] == '*')
                    {
                        if (index + 2 < normalized.Length && normalized[index + 2] == '/')
                        {
                            sb.Append("(?:.*/)?");
                            index += 2;
                        }
                        else
                        {
                            sb.Append(".*");
                            index++;
                        }
                    }
                    else
                    {
                        sb.Append("[^/]*");
                    }
                    continue;
                }

                if (ch == '[')
                {
                    var classEnd = FindCharacterClassEnd(normalized, index + 1);
                    if (classEnd > index + 1)
                    {
                        sb.Append(ConvertCharacterClass(normalized[(index + 1)..classEnd]));
                        index = classEnd;
                        continue;
                    }
                }

                if (ch == '?')
                {
                    sb.Append("[^/]");
                    continue;
                }

                sb.Append(Regex.Escape(ch.ToString()));
            }

            sb.Append('$');
            return new Regex(
                sb.ToString(),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                RegexTimeout);
        }

        private static IEnumerable<string> EnumerateParentPaths(string candidate)
        {
            var current = candidate;
            while (true)
            {
                var slashIndex = current.LastIndexOf('/');
                if (slashIndex <= 0)
                {
                    yield break;
                }

                current = current[..slashIndex];
                yield return current;
            }
        }

        private static int FindCharacterClassEnd(string pattern, int startIndex)
        {
            for (var index = startIndex; index < pattern.Length; index++)
            {
                if (pattern[index] == ']' && index > startIndex)
                {
                    return index;
                }
            }

            return -1;
        }

        private static string ConvertCharacterClass(string content)
        {
            var sb = new StringBuilder("[");
            var index = 0;

            if (content.Length > 0 && (content[0] == '!' || content[0] == '^'))
            {
                sb.Append('^');
                index = 1;
            }

            for (; index < content.Length; index++)
            {
                var ch = content[index];
                if (ch is '\\' or ']')
                {
                    sb.Append('\\');
                }

                sb.Append(ch);
            }

            sb.Append(']');
            return sb.ToString();
        }
    }
}
