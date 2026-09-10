using System;

namespace BoscaliSummer.Framework.Features
{
    internal sealed class FeatureMetadata
    {
        public string Id { get; }
        public string DisplayName { get; }
        public string[] Dependencies { get; }

        public FeatureMetadata(string id, string displayName, params string[] dependencies)
        {
            if (!FeatureId.IsValid(id))
                throw new ArgumentException("Feature IDs must use lowercase letters, digits, and single hyphens.", nameof(id));
            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("A feature display name is required.", nameof(displayName));

            Id = id;
            DisplayName = displayName;
            Dependencies = dependencies == null
                ? Array.Empty<string>()
                : (string[])dependencies.Clone();

            for (int i = 0; i < Dependencies.Length; i++)
            {
                string dependency = Dependencies[i];
                if (!FeatureId.IsValid(dependency))
                    throw new ArgumentException("Feature dependency IDs must be valid feature IDs.", nameof(dependencies));
                if (string.Equals(dependency, Id, StringComparison.Ordinal))
                    throw new ArgumentException("A feature cannot depend on itself.", nameof(dependencies));
            }
        }
    }

    internal static class FeatureId
    {
        public static bool IsValid(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 48 || value[0] == '-' || value[value.Length - 1] == '-')
                return false;

            bool previousHyphen = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool hyphen = c == '-';
                if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && !hyphen)
                    return false;
                if (hyphen && previousHyphen) return false;
                previousHyphen = hyphen;
            }
            return true;
        }
    }
}
