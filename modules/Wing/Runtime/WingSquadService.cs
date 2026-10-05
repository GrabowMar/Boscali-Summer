using System;
using BoscaliSummer.Core.Contracts;
using UnityEngine;

namespace BoscaliSummer.Modules.Wing.Runtime
{
    /// <summary>IWingSquad over the in-tree squadron: pilot generation, portraits, the
    /// saved-pilot studio, adversary flights and survivor tracking. All state lives in
    /// WingSquad; this class only adapts its fixed-position value arrays to contract
    /// views (same field order as the studio record comment there).</summary>
    internal sealed class WingSquadService : IWingSquad
    {
        public bool TryCreatePilot(int seed, out string name, out string callsign,
            out string background, out int persona)
        {
            name = callsign = background = string.Empty;
            persona = 0;
            object[] values = WingSquad.CreatePilot(seed);
            if (values == null || values.Length != 4 || !(values[0] is string pilotName) ||
                !(values[1] is string pilotCallsign) || !(values[2] is string pilotBackground) ||
                !(values[3] is int pilotPersona)) return false;
            name = pilotName;
            callsign = pilotCallsign;
            background = pilotBackground;
            persona = pilotPersona;
            return true;
        }

        public Sprite PilotPortrait(string name, string callsign) => WingSquad.Portrait(name, callsign);

        public Sprite PersonnelPortrait(string name, string callsign, PortraitRole role, int faction = -1) =>
            WingSquad.PersonnelPortrait(name, callsign, role, faction);

        public Aircraft[] SpawnWingAt(Aircraft target, FactionHQ enemyHq, int seed, int tier,
            int count, string callsign, float ingressX, float ingressZ) =>
            WingSquad.SpawnWingAt(target, enemyHq, seed, tier, count, callsign, ingressX, ingressZ);

        public bool SetWingTarget(Aircraft[] wing, Aircraft target) => WingSquad.SetTarget(wing, target);

        public void ReleaseWing(Aircraft[] wing, bool destroy) => WingSquad.ReleaseWing(wing, destroy);

        public void Chatter(string callsign, string context, string message) =>
            WingSquad.Chatter(callsign, context, message);

        public int SurvivorStatus(PersistentID aircraftId) => WingSquad.SurvivorStatus(aircraftId);

        public bool RecoverSurvivor(PersistentID aircraftId) => WingSquad.RecoverSurvivor(aircraftId);

        public int AbilityMask(Aircraft aircraft) => WingSquad.AbilityMask(aircraft);

        public int PortraitBodyCount => WingSquad.PortraitBodyCount;
        public int PortraitFaceCount => WingSquad.PortraitFaceCount;
        public int PortraitHairCount => WingSquad.PortraitHairCount;
        public int PortraitUniformCount => WingSquad.PortraitUniformCount;
        public int PortraitAccessoryCount => WingSquad.PortraitAccessoryCount;
        public int PortraitBackdropCount => WingSquad.PortraitBackdropCount;
        public string PortraitBodyLabel(int body) => WingSquad.PortraitBodyLabel(body);
        public string PortraitUniformLabel(int uniform) => WingSquad.PortraitUniformLabel(uniform);
        public string PortraitAccessoryLabel(int accessory) => WingSquad.PortraitAccessoryLabel(accessory);
        public string PortraitBackdropLabel(int backdrop) => WingSquad.PortraitBackdropLabel(backdrop);
        public string PersonaLabel(int persona) => WingSquad.PersonaLabel(persona);
        public string RankNameForXp(int xp) => WingSquad.RankNameForXp(xp);

        public Sprite PortraitForSelection(int body, int face, int hair, int uniform,
            int accessory, int backdrop, bool preview = false) =>
            WingSquad.PortraitForSelection(body, face, hair, uniform, accessory, backdrop, preview);

        public bool TryGetCustomPilot(string callsign, out CustomPilotView record)
        {
            record = default;
            return TryMap(WingSquad.GetCustomPilot(callsign), out record);
        }

        public CustomPilotView[] ListCustomPilots()
        {
            object[][] raw = WingSquad.GetCustomPilots();
            if (raw == null || raw.Length == 0) return Array.Empty<CustomPilotView>();
            var parsed = new System.Collections.Generic.List<CustomPilotView>(Math.Min(raw.Length, 128));
            for (int i = 0; i < raw.Length; i++)
                if (TryMap(raw[i], out CustomPilotView record)) parsed.Add(record);
            return parsed.ToArray();
        }

        public bool SaveCustomPilot(CustomPilotView record) => WingSquad.SaveCustomPilot(ToValues(record));

        public bool DeleteCustomPilot(string callsign) => WingSquad.DeleteCustomPilot(callsign);

        public bool IsPilotRecruited(string callsign) => WingSquad.IsPilotRecruited(callsign);

        public bool RecruitCustomPilot(string callsign) => WingSquad.RecruitCustomPilot(callsign);

        public bool DischargeCustomPilot(string callsign) => WingSquad.DischargeCustomPilot(callsign);

        public int ImportAllCustomPilots() => WingSquad.ImportAllCustomPilots();

        private static bool TryMap(object[] values, out CustomPilotView record)
        {
            record = default;
            if (values == null || values.Length < 15) return false;
            try
            {
                record = new CustomPilotView
                {
                    Name = values[0] as string ?? string.Empty,
                    Callsign = values[1] as string ?? string.Empty,
                    DialogueTag = values[2] as string ?? string.Empty,
                    Persona = Convert.ToInt32(values[3]),
                    Background = values[4] as string ?? string.Empty,
                    Xp = Convert.ToInt32(values[5]),
                    Kills = Convert.ToInt32(values[6]),
                    Sorties = Convert.ToInt32(values[7]),
                    HasPortrait = Convert.ToBoolean(values[8]),
                    Body = Convert.ToInt32(values[9]),
                    Face = Convert.ToInt32(values[10]),
                    Hair = Convert.ToInt32(values[11]),
                    Uniform = Convert.ToInt32(values[12]),
                    Accessory = Convert.ToInt32(values[13]),
                    Backdrop = Convert.ToInt32(values[14]),
                };
                return !string.IsNullOrEmpty(record.Callsign);
            }
            catch (Exception)
            {
                record = default;
                return false;
            }
        }

        private static object[] ToValues(CustomPilotView record) => new object[]
        {
            record.Name ?? string.Empty,
            record.Callsign ?? string.Empty,
            record.DialogueTag ?? string.Empty,
            record.Persona,
            record.Background ?? string.Empty,
            record.Xp,
            record.Kills,
            record.Sorties,
            record.HasPortrait,
            record.Body,
            record.Face,
            record.Hair,
            record.Uniform,
            record.Accessory,
            record.Backdrop,
        };
    }
}
