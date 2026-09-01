using System;
using System.Collections.Generic;

namespace BoscaliSummer.Framework.Features
{
    internal static class FeatureGraph
    {
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
