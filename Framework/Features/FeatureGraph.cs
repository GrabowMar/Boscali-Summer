using System;
using System.Collections.Generic;

namespace BoscaliSummer.Framework.Features
{
    internal static class FeatureGraph
    {
        /// <summary>
        /// For each feature, the id of a dependency that is not in the list (settings switched it
        /// off), or null when it can load. A feature built on a feature that cannot load cannot
        /// load either, so exclusion follows the chain.
        /// </summary>
        public static string[] MissingDependencies(IReadOnlyList<FeatureMetadata> features)
        {
            if (features == null) throw new ArgumentNullException(nameof(features));

            var missing = new string[features.Count];
            var loadable = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < features.Count; i++) loadable.Add(features[i].Id);

            for (bool changed = true; changed;)
            {
                changed = false;
                for (int i = 0; i < features.Count; i++)
                {
                    if (missing[i] != null) continue;
                    foreach (string dependency in features[i].Dependencies)
                    {
                        if (loadable.Contains(dependency)) continue;
                        missing[i] = dependency;
                        loadable.Remove(features[i].Id);
                        changed = true;
                        break;
                    }
                }
            }
            return missing;
        }

        public static int[] Sort(IReadOnlyList<FeatureMetadata> features)
        {
            if (features == null) throw new ArgumentNullException(nameof(features));

            var byId = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < features.Count; i++)
            {
                FeatureMetadata metadata = features[i] ??
                    throw new ArgumentException("Feature metadata cannot be null.", nameof(features));
                if (byId.ContainsKey(metadata.Id))
                    throw new InvalidOperationException("Duplicate feature ID: " + metadata.Id);
                byId.Add(metadata.Id, i);
            }

            var states = new byte[features.Count];
            var order = new int[features.Count];
            int cursor = 0;
            for (int i = 0; i < features.Count; i++)
                Visit(i, features, byId, states, order, ref cursor);
            return order;
        }

        private static void Visit(
            int index,
            IReadOnlyList<FeatureMetadata> features,
            Dictionary<string, int> byId,
            byte[] states,
            int[] order,
            ref int cursor)
        {
            if (states[index] == 2) return;
            if (states[index] == 1)
                throw new InvalidOperationException("Feature dependency cycle includes: " + features[index].Id);

            states[index] = 1;
            string[] dependencies = features[index].Dependencies;
            for (int i = 0; i < dependencies.Length; i++)
            {
                if (!byId.TryGetValue(dependencies[i], out int dependencyIndex))
                    throw new InvalidOperationException(
                        "Feature '" + features[index].Id + "' depends on missing feature '" + dependencies[i] + "'.");
                Visit(dependencyIndex, features, byId, states, order, ref cursor);
            }

            states[index] = 2;
            order[cursor++] = index;
        }
    }
}
