using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Presentation;
using UnityEngine;

namespace BoscaliSummer.Modules.Wing.Runtime
{
    /// <summary>IWingSquad over the in-tree squadron: pilot generation, portraits, the
    /// saved-pilot lookup, the WMC studio launcher, adversary flights and survivor tracking. Survivors, portraits and adversary flights live in
    /// WingSquad; the pilot generator and the saved-pilot view are built here.</summary>
    internal sealed class WingSquadService : IWingSquad
    {
        /// <summary>Name, callsign, background and persona of a pilot drawn from <paramref name="seed"/>. No roster recruitment.</summary>
        public bool TryCreatePilot(int seed, out string name, out string callsign,
            out string background, out int persona)
        {
            var random = new System.Random(seed);
            var kind = (ChatterPersona)random.Next(ChatterPersonas.Count);
            name = PilotIdentity.Name(random.Next);
            callsign = PilotIdentity.Callsign(random.Next, _ => false);
            background = PilotIdentity.Background(random.Next, kind);
            persona = (int)kind;
            return true;
        }

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

        public int AbilityMask(Aircraft aircraft) => WingSurvivalPerks.AceAbilityMask(aircraft);

        public Sprite PortraitForSelection(int body, int face, int hair, int uniform,
            int accessory, int backdrop, bool preview = false) =>
            WingSquad.PortraitForSelection(body, face, hair, uniform, accessory, backdrop, preview);

        public bool TryGetCustomPilot(string callsign, out CustomPilotView view)
        {
            view = default;
            CustomPilotRecord r = string.IsNullOrWhiteSpace(callsign) ? null : WingSavedPilots.Store.Find(callsign);
            if (r == null || string.IsNullOrEmpty(r.Callsign)) return false;
            PortraitSelection look = r.Selection;
            view = new CustomPilotView
            {
                Name = r.Name ?? string.Empty,
                Callsign = r.Callsign,
                DialogueTag = r.DialogueTag ?? string.Empty,
                Persona = (int)r.Persona,
                Background = r.Background ?? string.Empty,
                Xp = r.Xp,
                Kills = r.Kills,
                Sorties = r.Sorties,
                HasPortrait = r.HasCustomPortrait,
                Body = (int)look.Body,
                Face = look.Face,
                Hair = look.Hair,
                Uniform = look.Uniform,
                Accessory = look.Accessory,
                Backdrop = look.Backdrop,
            };
            return true;
        }

        public bool OpenPilotStudio()
        {
            WmcPanel panel = WmcPanel.Instance;
            if (panel == null) return false;
            panel.Open();
            panel.Show(WmcTabs.Wing);
            panel.WingPage?.ShowSubNamed("STUDIO");
            return true;
        }
    }
}
