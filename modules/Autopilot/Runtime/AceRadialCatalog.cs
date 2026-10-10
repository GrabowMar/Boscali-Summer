using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Autopilot.Domain;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;

namespace BoscaliSummer.Modules.Autopilot.Runtime
{
    /// <summary>
    /// What the interaction menu offers, rebuilt on every open so services registered by
    /// later modules are always current. The root is a ring of categories (ACE3 self
    /// interaction): the vanilla aircraft controls in <c>AceRadialCatalog.Aircraft.cs</c> and the
    /// Boscali integrations in <c>AceRadialCatalog.Boscali.cs</c>. Only the local player's own
    /// aircraft is ever acted on, and only through the same vanilla calls its own key
    /// bindings make; a category with nothing usable prunes itself.
    /// </summary>
    internal static partial class AceRadialCatalog
    {
        private const int MaxPageEntries = 8;

        public static AceRadialAction Build()
        {
            var root = new AceRadialAction("root", AircraftName()).WithIcon(AceIcon.Aircraft).WithStatus(CentreStatus);
            root.Add(Flight());
            root.Add(Lights());
            root.Add(Weapons());
            root.Add(Targets());
            root.Add(Defence());
            root.Add(View());
            root.Add(Support());
            root.Add(Radio());
            root.Add(Comms());
            root.Add(Wing());
            return root;
        }

        /// <summary>The centre's live line: fuel, gear, selected targets and wing size at a glance.</summary>
        private static AceRadialStatus CentreStatus()
        {
            if (!TryAircraft(out Aircraft a)) return AceRadialStatus.None;
            float fuel = Math.Max(0f, Math.Min(1f, a.fuelLevel));
            string line = "FUEL " + (int)Math.Round(fuel * 100f) + "%";
            if (a.gearState == LandingGear.GearState.LockedExtended) line += " · GEAR DN";
            else if (a.gearState == LandingGear.GearState.Extending || a.gearState == LandingGear.GearState.Retracting) line += " · GEAR MOVING";
            int targets = TargetCount();
            if (targets > 0) line += " · " + targets + " TGT";
            int wing = Service<IWingOrders>()?.Members ?? 0;
            if (wing > 0) line += " · WING " + wing;
            return new AceRadialStatus(line, fuel < 0.2f ? AceTone.Caution : AceTone.Normal);
        }

        // ------------------------------------------------------------------ helpers

        private static bool TryAircraft(out Aircraft aircraft) =>
            GameManager.GetLocalAircraft(out aircraft) && aircraft != null && !aircraft.disabled;

        /// <summary>Visible/enabled condition over the live local aircraft.</summary>
        private static Func<bool> When(Func<Aircraft, bool> condition) =>
            () => TryAircraft(out Aircraft aircraft) && condition(aircraft);

        /// <summary>Statement over the live local aircraft, looked up again at run time.</summary>
        private static Action With(Action<Aircraft> run) =>
            () => { if (TryAircraft(out Aircraft aircraft)) run(aircraft); };

        /// <summary>State line over the live local aircraft.</summary>
        private static Func<AceRadialStatus> Read(Func<Aircraft, AceRadialStatus> status) =>
            () => TryAircraft(out Aircraft aircraft) ? status(aircraft) : AceRadialStatus.None;

        private static AceRadialAction Branch(string id, string label, AceIcon icon) =>
            new AceRadialAction(id, label).WithIcon(icon);

        private static AceRadialAction Leaf(string id, string label, AceIcon icon, Action run,
            Func<bool> visible = null, Func<bool> enabled = null, Func<AceRadialStatus> status = null) =>
            new AceRadialAction(id, label, run, visible, enabled).WithIcon(icon).WithStatus(status);

        private static T Service<T>() where T : class =>
            ModuleServices.TryGet(out T service) ? service : null;

        private static string AircraftName()
        {
            if (!TryAircraft(out Aircraft aircraft) || aircraft.definition == null) return "SELF";
            string name = aircraft.definition.unitName;
            return string.IsNullOrEmpty(name) ? "SELF" : name.ToUpperInvariant();
        }

        private static string Upper(string text, int max = 22)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string upper = text.ToUpperInvariant();
            return upper.Length <= max ? upper : upper.Substring(0, max - 1) + "…";
        }

        private static IEnumerable<AceRadialAction> ContributedPage()
        {
            if (!ModuleServices.TryGet(out IRadialMenuPage page)) yield break;
            var branch = Branch("page", Upper(page.Title ?? "PAGE"), AceIcon.Preset);
            int count = Math.Min(page.EntryCount, MaxPageEntries);
            for (int i = 0; i < count; i++)
            {
                int index = i;
                string label = page.EntryLabel(index);
                if (string.IsNullOrEmpty(label)) continue;
                branch.Add(Leaf("e" + index, Upper(label), AceIcon.Preset,
                    () => page.InvokeEntry(index), enabled: () => page.EntryAllowed(index)));
            }
            yield return branch;
        }
    }
}
