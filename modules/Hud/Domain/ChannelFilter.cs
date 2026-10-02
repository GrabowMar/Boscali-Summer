using System.Collections.Generic;
using System.Text;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>
    /// The parsed form of the `DisabledChannels` setting: a CSV of channel keys the pilot muted.
    /// Pure so the settings type and the migration rule can both be tested without a config file.
    /// The historic "Weather" token is canonicalised to "weather" on parse, matching the channel
    /// rename in slice 3 — an old config still mutes the renamed channel.
    /// </summary>
    internal sealed class ChannelFilter
    {
        private readonly List<string> _disabled = new List<string>();

        public void Parse(string csv)
        {
            _disabled.Clear();
            if (string.IsNullOrEmpty(csv)) return;

            string[] parts = csv.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string token = parts[i].Trim();
                if (token.Length == 0) continue;
                if (token == "Weather") token = "weather";
                if (!_disabled.Contains(token)) _disabled.Add(token);
            }
        }

        /// <summary>A blank or null key (no channel declared) always reads as enabled.</summary>
        public bool Enabled(string key)
        {
            if (string.IsNullOrEmpty(key)) return true;
            return !_disabled.Contains(key);
        }

        /// <summary>The CSV this filter would parse to after enabling/disabling <paramref name="key"/>, without mutating this instance.</summary>
        public string With(string key, bool enabled)
        {
            List<string> copy = new List<string>(_disabled);
            if (enabled)
            {
                copy.Remove(key);
            }
            else if (!copy.Contains(key))
            {
                copy.Add(key);
            }

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < copy.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(copy[i]);
            }
            return sb.ToString();
        }
    }
}
