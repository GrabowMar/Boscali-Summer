using System;
using System.Collections.Generic;
using System.Text;

using BoscaliSummer.Core.Util;
namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>Decoded custom-pilot data.</summary>
    internal sealed class CustomPilotRecord
    {
        public string Name { get; set; } = "UNKNOWN";
        public string Callsign { get; set; } = "PILOT";
        public string DialogueTag { get; set; }
        public ChatterPersona Persona { get; set; } = ChatterPersona.Professional;
        public string Background { get; set; } = "";
        public int Xp { get; set; }
        public int Kills { get; set; }
        public int Sorties { get; set; }
        /// <summary>Missions flown by a saved pilot (R7's service record; xp is then the best XP of one mission).</summary>
        public int Missions { get; set; }
        public int PortraitVersion { get; set; } = 3;
        public PortraitBody Body { get; set; } = PortraitBody.Male;
        public int Face { get; set; } = -1;
        public int Hair { get; set; }
        public int Uniform { get; set; }
        public int Accessory { get; set; }
        public int Backdrop { get; set; }

        public bool HasCustomPortrait => Face >= 0;

        /// <summary>Canonical selection. This is safe to persist and pass through live roster state.</summary>
        public PortraitSelection Selection => PilotPortraitGenerator.Normalize(
            new PortraitSelection(Body, Face, Hair, Uniform, Accessory, Backdrop));

        public void ApplySelection(PortraitSelection selection)
        {
            selection = PilotPortraitGenerator.Normalize(selection);
            PortraitVersion = 3;
            Body = selection.Body;
            Face = selection.Face;
            Hair = selection.Hair;
            Uniform = selection.Uniform;
            Accessory = selection.Accessory;
            Backdrop = selection.Backdrop;
        }

        public string ResolvedDialogueTag =>
            !string.IsNullOrWhiteSpace(DialogueTag) ? DialogueTag.Trim().ToUpperInvariant() : Callsign.Trim().ToUpperInvariant();

        public CustomPilotRecord Clone(string newCallsign = null)
        {
            return new CustomPilotRecord
            {
                Name = Name,
                Callsign = newCallsign ?? Callsign,
                DialogueTag = DialogueTag,
                Persona = Persona,
                Background = Background,
                Xp = Xp,
                Kills = Kills,
                Sorties = Sorties,
                Missions = Missions,
                PortraitVersion = PortraitVersion,
                Body = Body,
                Face = Face,
                Hair = Hair,
                Uniform = Uniform,
                Accessory = Accessory,
                Backdrop = Backdrop,
            };
        }
    }

    /// <summary>Pilots decoded from one file.</summary>
    internal sealed class CustomPilotPayload
    {
        public List<CustomPilotRecord> Pilots { get; } = new List<CustomPilotRecord>();
    }

    /// <summary>Dependency-free custom-pilot JSON decoder accepting comments, optional fields, and case
    /// variations; malformed input is ignored.</summary>
    internal static class CustomPilotCodec
    {
        public static CustomPilotPayload Decode(string json)
        {
            var payload = new CustomPilotPayload();
            if (string.IsNullOrWhiteSpace(json)) return payload;

            if (!MiniJson.TryParse(json, out object root)) return payload;

            if (root is Dictionary<string, object> dict)
            {
                if (MiniJson.TryGetList(dict, "pilots", out List<object> pilotList))
                {
                    foreach (object item in pilotList)
                    {
                        if (item is Dictionary<string, object> pilotDict)
                        {
                            CustomPilotRecord record = ParsePilot(pilotDict);
                            if (record != null) payload.Pilots.Add(record);
                        }
                    }
                }
                else if (dict.ContainsKey("callsign") || dict.ContainsKey("name"))
                {
                    CustomPilotRecord record = ParsePilot(dict);
                    if (record != null) payload.Pilots.Add(record);
                }
            }
            else if (root is List<object> list)
            {
                foreach (object item in list)
                {
                    if (item is Dictionary<string, object> pilotDict)
                    {
                        CustomPilotRecord record = ParsePilot(pilotDict);
                        if (record != null) payload.Pilots.Add(record);
                    }
                }
            }

            return payload;
        }

        private static CustomPilotRecord ParsePilot(Dictionary<string, object> dict)
        {
            string callsign = MiniJson.GetString(dict, "callsign");
            if (string.IsNullOrWhiteSpace(callsign)) return null;

            string name = MiniJson.GetString(dict, "name");
            if (string.IsNullOrWhiteSpace(name)) name = callsign;

            string tag = MiniJson.GetString(dict, "dialoguetag");
            if (string.IsNullOrWhiteSpace(tag)) tag = callsign.ToUpperInvariant();

            string personaStr = MiniJson.GetString(dict, "persona");
            ChatterPersona persona = ChatterPersona.Professional;
            if (!string.IsNullOrWhiteSpace(personaStr))
            {
                if (Enum.TryParse(personaStr, ignoreCase: true, out ChatterPersona parsed))
                    persona = parsed;
            }

            string background = MiniJson.GetString(dict, "background") ?? "";
            int xp = MiniJson.GetInt(dict, "xp", 0);
            int kills = MiniJson.GetInt(dict, "kills", 0);
            int sorties = MiniJson.GetInt(dict, "sorties", 0);
            int missions = MiniJson.GetInt(dict, "missions", 0);

            int face = MiniJson.GetInt(dict, "face", -1);
            int hair = MiniJson.GetInt(dict, "hair", -1);
            int uniform = MiniJson.GetInt(dict, "uniform", -1);
            int accessory = MiniJson.GetInt(dict, "accessory", 0);
            int backdrop = MiniJson.GetInt(dict, "backdrop", -1);
            int portraitVersion = MiniJson.GetInt(dict, "portraitVersion", 0);
            PortraitBody body = string.Equals(MiniJson.GetString(dict, "body"), "female", StringComparison.OrdinalIgnoreCase)
                ? PortraitBody.Female
                : PortraitBody.Male;

            var record = new CustomPilotRecord
            {
                Name = name.Trim(),
                Callsign = callsign.Trim().ToUpperInvariant(),
                DialogueTag = tag.Trim().ToUpperInvariant(),
                Persona = persona,
                Background = background.Trim(),
                Xp = Math.Max(0, xp),
                Kills = Math.Max(0, kills),
                Sorties = Math.Max(0, sorties),
                Missions = Math.Max(0, missions),
            };

            if (face >= 0)
            {
                record.ApplySelection(new PortraitSelection(body, face, hair, uniform, portraitVersion >= 3 ? accessory : 0, backdrop));
            }
            return record;
        }

        public static string Encode(IEnumerable<CustomPilotRecord> pilots)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"pilots\": [");

            bool firstPilot = true;
            if (pilots != null)
            {
                foreach (CustomPilotRecord p in pilots)
                {
                    if (p == null || string.IsNullOrWhiteSpace(p.Callsign)) continue;
                    if (!firstPilot) sb.AppendLine(",");
                    firstPilot = false;

                    sb.AppendLine("    {");
                    sb.AppendLine($"      \"name\": \"{MiniJson.Escape(p.Name)}\",");
                    sb.AppendLine($"      \"callsign\": \"{MiniJson.Escape(p.Callsign)}\",");
                    sb.AppendLine($"      \"dialogueTag\": \"{MiniJson.Escape(p.ResolvedDialogueTag)}\",");
                    sb.AppendLine($"      \"persona\": \"{p.Persona}\",");
                    sb.AppendLine($"      \"background\": \"{MiniJson.Escape(p.Background)}\",");
                    sb.AppendLine($"      \"xp\": {p.Xp},");
                    sb.AppendLine($"      \"kills\": {p.Kills},");
                    sb.AppendLine($"      \"sorties\": {p.Sorties},");
                    sb.Append($"      \"missions\": {p.Missions}");

                    if (p.HasCustomPortrait)
                    {
                        PortraitSelection selection = p.Selection;
                        sb.AppendLine(",");
                        sb.AppendLine("      \"portraitVersion\": 3,");
                        sb.AppendLine($"      \"body\": \"{PilotPortraitGenerator.BodyLabel(selection.Body).ToLowerInvariant()}\",");
                        sb.AppendLine($"      \"face\": {selection.Face},");
                        sb.AppendLine($"      \"hair\": {selection.Hair},");
                        sb.AppendLine($"      \"uniform\": {selection.Uniform},");
                        sb.AppendLine($"      \"accessory\": {selection.Accessory},");
                        sb.Append($"      \"backdrop\": {selection.Backdrop}");
                    }
                    sb.AppendLine();
                    sb.Append("    }");
                }
            }
            sb.AppendLine();
            sb.Append("  ]");

            sb.AppendLine();
            sb.AppendLine("}");
            return sb.ToString();
        }
    }
}
