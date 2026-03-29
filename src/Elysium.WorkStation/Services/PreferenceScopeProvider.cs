using Elysium.WorkStation.Models;

namespace Elysium.WorkStation.Services
{
    public enum PreferenceGroup
    {
        Release = 0,
        DebugClient = 1,
        DebugServer = 2
    }

    public static class PreferenceScopeProvider
    {
        public const string ReleaseGroupName = "release";
        public const string DebugClientGroupName = "debug.client";
        public const string DebugServerGroupName = "debug.server";

        private const string DebugGroupSelectorKey = "__debug.preference.group";
#if DEBUG
        private static readonly object ScopeGate = new();
        private static PreferenceGroup _runtimeGroup = LoadStoredDebugGroup();
#endif

        public static PreferenceGroup CurrentGroup
        {
            get
            {
#if DEBUG
                lock (ScopeGate)
                {
                    return _runtimeGroup;
                }
#else
                return PreferenceGroup.Release;
#endif
            }
        }

        public static string CurrentGroupName => CurrentGroup switch
        {
            PreferenceGroup.DebugClient => DebugClientGroupName,
            PreferenceGroup.DebugServer => DebugServerGroupName,
            _ => ReleaseGroupName
        };

        public static string BuildScopedKey(string key)
        {
            if (CurrentGroup == PreferenceGroup.Release)
            {
                return key;
            }

            return $"{CurrentGroupName}.{key}";
        }

        public static void SetDebugRole(AppRole role)
        {
#if DEBUG
            var nextGroup = role == AppRole.Server
                ? PreferenceGroup.DebugServer
                : PreferenceGroup.DebugClient;

            lock (ScopeGate)
            {
                _runtimeGroup = nextGroup;
            }

            Preferences.Default.Set(DebugGroupSelectorKey, ToGroupName(nextGroup));
#endif
        }

#if DEBUG
        private static PreferenceGroup LoadStoredDebugGroup()
        {
            var selected = Preferences.Default.Get(DebugGroupSelectorKey, DebugClientGroupName);
            return string.Equals(selected, DebugServerGroupName, StringComparison.OrdinalIgnoreCase)
                ? PreferenceGroup.DebugServer
                : PreferenceGroup.DebugClient;
        }

        private static string ToGroupName(PreferenceGroup group)
        {
            return group switch
            {
                PreferenceGroup.DebugServer => DebugServerGroupName,
                PreferenceGroup.DebugClient => DebugClientGroupName,
                _ => ReleaseGroupName
            };
        }
#endif
    }

    public static class ScopedPreferences
    {
        public static T Get<T>(string key, T defaultValue)
        {
            return Preferences.Default.Get(PreferenceScopeProvider.BuildScopedKey(key), defaultValue);
        }

        public static void Set<T>(string key, T value)
        {
            Preferences.Default.Set(PreferenceScopeProvider.BuildScopedKey(key), value);
        }

        public static void Remove(string key)
        {
            Preferences.Default.Remove(PreferenceScopeProvider.BuildScopedKey(key));
        }
    }
}
