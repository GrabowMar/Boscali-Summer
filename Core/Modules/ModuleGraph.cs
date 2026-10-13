using System;
using System.Collections.Generic;

namespace BoscaliSummer.Core.Modules
{
    internal static class ModuleGraph
    {
        /// <summary>
        /// For each module, the id of a dependency that is not in the list (settings switched it
        /// off), or null when it can load. A module built on a module that cannot load cannot
        /// load either, so exclusion follows the chain.
        /// </summary>
        public static string[] MissingDependencies(IReadOnlyList<ModuleMetadata> modules,
            IReadOnlyCollection<string> available = null)
        {
            if (modules == null) throw new ArgumentNullException(nameof(modules));

            var missing = new string[modules.Count];
            var loadable = new HashSet<string>(StringComparer.Ordinal);
            if (available != null) loadable.UnionWith(available);
            for (int i = 0; i < modules.Count; i++)
                if (!loadable.Add(modules[i].Id))
                    throw new InvalidOperationException("Duplicate module ID: " + modules[i].Id);

            for (bool changed = true; changed;)
            {
                changed = false;
                for (int i = 0; i < modules.Count; i++)
                {
                    if (missing[i] != null) continue;
                    foreach (string dependency in modules[i].Dependencies)
                    {
                        if (loadable.Contains(dependency)) continue;
                        missing[i] = dependency;
                        loadable.Remove(modules[i].Id);
                        changed = true;
                        break;
                    }
                }
            }
            return missing;
        }

        public static int[] Sort(IReadOnlyList<ModuleMetadata> modules,
            IReadOnlyCollection<string> available = null)
        {
            if (modules == null) throw new ArgumentNullException(nameof(modules));

            var byId = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < modules.Count; i++)
            {
                ModuleMetadata metadata = modules[i] ??
                    throw new ArgumentException("Module metadata cannot be null.", nameof(modules));
                if (byId.ContainsKey(metadata.Id))
                    throw new InvalidOperationException("Duplicate module ID: " + metadata.Id);
                byId.Add(metadata.Id, i);
            }

            var states = new byte[modules.Count];
            var order = new int[modules.Count];
            int cursor = 0;
            var existing = available == null ? null : new HashSet<string>(available, StringComparer.Ordinal);
            for (int i = 0; i < modules.Count; i++)
                Visit(i, modules, byId, existing, states, order, ref cursor);
            return order;
        }

        private static void Visit(
            int index,
            IReadOnlyList<ModuleMetadata> modules,
            Dictionary<string, int> byId,
            HashSet<string> existing,
            byte[] states,
            int[] order,
            ref int cursor)
        {
            if (states[index] == 2) return;
            if (states[index] == 1)
                throw new InvalidOperationException("Module dependency cycle includes: " + modules[index].Id);

            states[index] = 1;
            string[] dependencies = modules[index].Dependencies;
            for (int i = 0; i < dependencies.Length; i++)
            {
                if (!byId.TryGetValue(dependencies[i], out int dependencyIndex))
                {
                    if (existing != null && existing.Contains(dependencies[i])) continue;
                    throw new InvalidOperationException(
                        "Module '" + modules[index].Id + "' depends on missing module '" + dependencies[i] + "'.");
                }
                Visit(dependencyIndex, modules, byId, existing, states, order, ref cursor);
            }

            states[index] = 2;
            order[cursor++] = index;
        }
    }
}
