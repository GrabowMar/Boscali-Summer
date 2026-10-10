using System.Collections.Generic;
using BoscaliSummer.Modules.Hud.Domain;
using HarmonyLib;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// Reads the HUD aircraft's lasting damage for the board's damage lamps (2026-10-10), the way the vanilla status
    /// display does it but without its ten-second fade: <c>IEngine.OnEngineDamage</c> / <c>OnEngineDisable</c> are latched
    /// for the sortie (one subscription per engine, dropped on <see cref="Release"/>); the fuel tanks' private
    /// <c>isLeaking</c> / <c>ruptured</c> / <c>onFire</c> flags are read through cached field refs; the structure left is
    /// <c>PartDamageTracker.GetDetachedRatio</c> (the game throttles it to once a second). Client-local, read-only.
    /// </summary>
    internal sealed class DamageWatch
    {
        private static readonly AccessTools.FieldRef<FuelTank, bool> Leaking = NativeRefs.Field<FuelTank, bool>("isLeaking");
        private static readonly AccessTools.FieldRef<FuelTank, bool> Ruptured = NativeRefs.Field<FuelTank, bool>("ruptured");
        private static readonly AccessTools.FieldRef<FuelTank, bool> OnFire = NativeRefs.Field<FuelTank, bool>("onFire");

        private readonly List<IEngine> engines = new List<IEngine>(4);
        private Aircraft watched;
        private bool engineDamaged;
        private int enginesOut;

        public void Read(Aircraft aircraft, BoardFrame frame)
        {
            frame.Damage = default;
            if (aircraft == null || aircraft.disabled)
            {
                Swap(null);
                return;
            }
            if (aircraft != watched || engines.Count != aircraft.engineStates.Count) Swap(aircraft);

            var state = new DamageState
            {
                Flags = engineDamaged ? DamageFlags.EngineDamaged : DamageFlags.None,
                Engines = engines.Count,
                EnginesOut = System.Math.Min(enginesOut, engines.Count),
            };
            List<FuelTank> tanks = aircraft.GetFuelTanks();
            for (int i = 0; tanks != null && i < tanks.Count; i++)
            {
                FuelTank tank = tanks[i];
                if (tank == null) continue;
                if (NativeRefs.Get(Leaking, tank) || NativeRefs.Get(Ruptured, tank)) state.Flags |= DamageFlags.FuelLeak;
                if (NativeRefs.Get(OnFire, tank)) state.Flags |= DamageFlags.Fire;
            }
            if (aircraft.partDamageTracker != null) state.DetachedRatio = aircraft.partDamageTracker.GetDetachedRatio();
            frame.Damage = state;
        }

        public void Release() => Swap(null);

        private void Swap(Aircraft aircraft)
        {
            foreach (IEngine engine in engines)
            {
                if (engine == null) continue;
                engine.OnEngineDamage -= OnDamage;
                engine.OnEngineDisable -= OnDisable;
            }
            engines.Clear();
            if (aircraft != watched) { engineDamaged = false; enginesOut = 0; }
            watched = aircraft;
            if (aircraft == null) return;
            foreach (IEngine engine in aircraft.engineStates)
            {
                if (engine == null) continue;
                engine.OnEngineDamage += OnDamage;
                engine.OnEngineDisable += OnDisable;
                engines.Add(engine);
            }
        }

        private void OnDamage() => engineDamaged = true;

        private void OnDisable() => enginesOut++;
    }
}
