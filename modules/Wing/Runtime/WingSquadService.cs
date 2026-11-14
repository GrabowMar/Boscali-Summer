using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Presentation;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Wing.Runtime
{
    /// <summary>IWingSquad over the in-tree squadron: pilot generation, portraits, the
    /// saved-pilot lookup, the squadron page PILOT STUDIO hosts, adversary flights and survivor tracking. Survivors, portraits and adversary flights live in
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
                File = PilotPortrait.FromFile(r.Callsign),
            };
            return true;
        }

        public string[] SavedPilotCallsigns()
        {
            var records = WingSavedPilots.Store.Records;
            var names = new string[records.Count];
            for (int i = 0; i < names.Length; i++) names[i] = records[i].Callsign;
            return names;
        }

        public bool OpenPilotStudio()
        {
            AvConsole.Open("PILOT", "STUDIO");
            squadron?.ShowSubNamed("STUDIO");
            return true;
        }

        // ponytail: one hosted squadron (the latest PILOT build); the console it lived on takes the old one with it.
        private WmcWing squadron;

        public void BuildSquadron(int page, AvFlow flow, AvConsole console)
        {
            var s = squadron = new WmcWing();
            s.Build(flow, flow.Ticker, page);
            // The WMC's context (rows, wing, client) is filled bezel or not; the page refreshes at the ticker's rate while it shows.
            flow.Ticker.Add(page, AvTickRate.Fast, () =>
            {
                WmcPanel panel = WmcPanel.Instance;
                if (panel == null) return;
                panel.FillContext();
                s.Refresh(panel.Context);
            });
            // As the WMC did on a tab change: a studio field lets go of the keyboard and a pick list closes.
            console.PageChanged += _ => { WmcNameField.BlurAny(); AvPopup.CloseAny(); };
        }

        public string SquadronLine => squadron == null ? null
            : squadron.Alert ?? (WingToast.Last != null && Time.unscaledTime - WingToast.LastAt < 6f ? WingToast.Last : squadron.Hint);
    }
}
