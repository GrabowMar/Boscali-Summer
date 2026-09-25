using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace BoscaliSummer.Infrastructure.Diagnostics
{
    /// <summary>
    /// Argument and result plumbing shared by the <c>*Automation</c> hooks that nomodkit's
    /// <c>nomod sim run</c> calls by name. Arguments arrive as a JSON-decoded dictionary, so a
    /// number may be a double or a string and any key may be missing; every read falls back
    /// instead of throwing.
    /// </summary>
    internal static class AutomationArgs
    {
        public static object Arg(Dictionary<string, object> args, string key) =>
            args != null && args.TryGetValue(key, out object value) ? value : null;

        public static bool Has(Dictionary<string, object> args, string key) => args != null && args.ContainsKey(key);

        public static string Text(Dictionary<string, object> args, string key) => Arg(args, key) as string;

        public static bool Bool(Dictionary<string, object> args, string key)
        {
            try { return Convert.ToBoolean(Arg(args, key), CultureInfo.InvariantCulture); }
            catch (Exception) { return false; }
        }

        public static float Number(Dictionary<string, object> args, string key, float fallback)
        {
            object value = Arg(args, key);
            if (value == null) return fallback;
            try { return Convert.ToSingle(value, CultureInfo.InvariantCulture); }
            catch (Exception) { return fallback; }
        }

        /// <summary>One log line for a readout: <c>key=value, key=value</c>.</summary>
        public static string Describe(Dictionary<string, object> state)
        {
            var parts = new List<string>(state.Count);
            foreach (KeyValuePair<string, object> pair in state)
                parts.Add(pair.Key + "=" + Convert.ToString(pair.Value, CultureInfo.InvariantCulture));
            return string.Join(", ", parts);
        }

        /// <summary>The failure result every hook returns: <c>{"ok": false, "error": ...}</c>, logged under <paramref name="source"/>.</summary>
        public static Dictionary<string, object> Failure(string source, string hook, string error)
        {
            Debug.LogWarning("[" + source + "] " + hook + ": " + error);
            return new Dictionary<string, object> { { "ok", false }, { "error", error } };
        }
    }
}
