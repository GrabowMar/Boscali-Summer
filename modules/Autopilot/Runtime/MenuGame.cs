using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Autopilot.Domain;

namespace BoscaliSummer.Modules.Autopilot.Runtime
{
    /// <summary>
    /// What the interaction menu offers, rebuilt on every open and every 0.25 s while it is open so state lines stay live.
    /// This is only the adapter: <c>Read*</c> fills a plain <see cref="MenuWorld"/> from the game and from the other modules' contracts
    /// (vanilla controls in <c>MenuGame.Aircraft.cs</c>, the Boscali integrations in <c>MenuGame.Contracts.cs</c>),
    /// <see cref="MenuCatalog"/> (pure, tested offline) builds the model from it, and <c>Bind</c> attaches the game call to every runnable
    /// row by its stable id. Only the local player's own aircraft is ever acted on, and only through the same vanilla calls its own key
    /// bindings make. One reader throwing drops only its own category's data.
    /// </summary>
    internal static partial class MenuGame
    {
        private static readonly HashSet<string> Warned = new HashSet<string>();

        /// <summary>Throws without a live local aircraft: every reader would return early and the catalog would show its fabricated default world.</summary>
        public static MenuModel Build()
        {
            if (!TryAircraft(out _)) throw new InvalidOperationException("no live local aircraft");
            return MenuCatalog.Build(ReadWorld(), Bind);
        }

        /// <summary>Drops cached references to the old scene's aircraft.</summary>
        public static void Reset() => ResetAircraftCache();

        private static MenuWorld ReadWorld()
        {
            var w = new MenuWorld();
            Guarded(w, "aircraft", ReadAircraft);
            Guarded(w, "flight", ReadFlight);
            Guarded(w, "lights", ReadLights);
            Guarded(w, "weapons", ReadWeapons);
            Guarded(w, "defence", ReadDefence);
            Guarded(w, "view", ReadView);
            Guarded(w, "targets", ReadTargets);
            Guarded(w, "support", ReadSupport);
            Guarded(w, "radio", ReadRadio);
            Guarded(w, "comms", ReadComms);
            Guarded(w, "wing", ReadWing);
            Guarded(w, "filters", ReadFilters);
            return w;
        }

        private static void Guarded(MenuWorld w, string name, Action<MenuWorld> read)
        {
            try { read(w); }
            catch (Exception e)
            {
                if (Warned.Add(name)) Plugin.Logger?.LogWarning("Autopilot: interaction menu " + name + " unavailable (" + e.Message + ")");
            }
        }

        // ------------------------------------------------------------------ helpers

        private static bool TryAircraft(out Aircraft aircraft) =>
            GameManager.GetLocalAircraft(out aircraft) && aircraft != null && !aircraft.disabled;

        /// <summary>Statement over the live local aircraft, looked up again at run time.</summary>
        private static Action With(Action<Aircraft> run) =>
            () => { if (TryAircraft(out Aircraft aircraft)) run(aircraft); };

        private static T Service<T>() where T : class =>
            ModuleServices.TryGet(out T service) ? service : null;
    }
}
