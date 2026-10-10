using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Hud.Domain;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// Reads the HUD aircraft's lasting damage for the SYSTEMS page (2026-10-10), the way the vanilla status display does it but
    /// without its ten-second fade: <c>IEngine.OnEngineDamage</c> / <c>OnEngineDisable</c> are latched for the sortie (one subscription
    /// per engine, dropped on <see cref="Release"/>); the fuel tanks' private <c>isLeaking</c> / <c>ruptured</c> / <c>onFire</c> flags are
    /// read through cached field refs; the structure left is <c>PartDamageTracker.GetDetachedRatio</c> (the game throttles it to once a
    /// second). Each of these also says WHERE, as <see cref="ZoneSide"/> masks seen from above (left / centre / right of the airframe's
    /// own axis): an engine by its transform, a tank by its transform, a lost part by the side it was fitted on (its place is read once
    /// per aircraft, because a detached part flies off). Client-local, read-only.
    /// </summary>
    internal sealed class DamageWatch
    {
        private static readonly AccessTools.FieldRef<FuelTank, bool> Leaking = NativeRefs.Field<FuelTank, bool>("isLeaking");
        private static readonly AccessTools.FieldRef<FuelTank, bool> Ruptured = NativeRefs.Field<FuelTank, bool>("ruptured");
        private static readonly AccessTools.FieldRef<FuelTank, bool> OnFire = NativeRefs.Field<FuelTank, bool>("onFire");

        /// <summary>Metres either side of the axis that still count as the centreline: an engine or a tank, and a part that was a wing.</summary>
        private const float EngineBand = 0.4f, TankBand = 1f, WingBand = 1.5f;
        private const int MaxEngines = 8;

        private readonly List<IEngine> engines = new List<IEngine>(4);
        private readonly Action[] disabled = new Action[MaxEngines];
        private readonly byte[] engineSide = new byte[MaxEngines];
        private readonly bool[] engineOut = new bool[MaxEngines];
        private float[] partX = Array.Empty<float>();
        private Aircraft watched;
        private bool engineDamaged;
        private int enginesOut, engineCount;

        /// <summary>The airframe is a helicopter (it carries a rotor shaft): the SYSTEMS page draws a rotary silhouette.</summary>
        public bool Rotary { get; private set; }

        public void Read(Aircraft aircraft, BoardFrame frame)
        {
            frame.Damage = default;
            if (aircraft == null || aircraft.disabled)
            {
                Swap(null);
                return;
            }
            if (aircraft != watched || engineCount != aircraft.engineStates.Count) Swap(aircraft);   // the raw count: a null entry is not in `engines`

            var state = new DamageState
            {
                Flags = engineDamaged ? DamageFlags.EngineDamaged : DamageFlags.None,
                Engines = engines.Count,
                EnginesOut = Math.Min(enginesOut, engines.Count),
            };
            for (int i = 0; i < engines.Count; i++)
            {
                byte bit = engineSide[i];
                state.EngineSlots |= bit;
                if (engineOut[i]) state.EngineOut |= bit;
            }
            Transform own = aircraft.transform;
            List<FuelTank> tanks = aircraft.GetFuelTanks();
            for (int i = 0; tanks != null && i < tanks.Count; i++)
            {
                FuelTank tank = tanks[i];
                if (tank == null) continue;
                byte side = SideOf(own, tank.transform.position, TankBand);
                if (NativeRefs.Get(Leaking, tank) || NativeRefs.Get(Ruptured, tank)) { state.Flags |= DamageFlags.FuelLeak; state.Leak |= side; }
                if (NativeRefs.Get(OnFire, tank)) { state.Flags |= DamageFlags.Fire; state.Fire |= side; }
            }
            if (aircraft.partDamageTracker != null) state.DetachedRatio = aircraft.partDamageTracker.GetDetachedRatio();
            if (state.DetachedRatio > 0f) state.WingLoss = LostWings(aircraft);
            frame.Damage = state;
        }

        public void Release() => Swap(null);

        /// <summary>The wings that lost a part: any detached part that was fitted beyond the wing band, by the side it was fitted on.</summary>
        private byte LostWings(Aircraft aircraft)
        {
            List<UnitPart> parts = aircraft.partLookup;
            byte mask = 0;
            for (int i = 0; parts != null && i < parts.Count && i < partX.Length; i++)
            {
                UnitPart part = parts[i];
                if (part == null || Math.Abs(partX[i]) < WingBand || !part.IsDetached()) continue;
                mask |= partX[i] < 0f ? (byte)ZoneSide.Left : (byte)ZoneSide.Right;
            }
            return mask;
        }

        private static byte SideOf(Transform own, Vector3 world, float band) => SystemsMath.SideOf(own.InverseTransformPoint(world).x, band);

        private void Swap(Aircraft aircraft)
        {
            for (int i = 0; i < engines.Count; i++)
            {
                if (engines[i] == null) continue;
                engines[i].OnEngineDamage -= OnDamage;
                if (disabled[i] != null) engines[i].OnEngineDisable -= disabled[i];
                disabled[i] = null;
            }
            engines.Clear();
            if (aircraft != watched)
            {
                engineDamaged = false;
                enginesOut = 0;
                Array.Clear(engineOut, 0, engineOut.Length);
                partX = Array.Empty<float>();
                Rotary = false;
            }
            watched = aircraft;
            engineCount = aircraft != null ? aircraft.engineStates.Count : 0;
            if (aircraft == null) return;
            foreach (IEngine engine in aircraft.engineStates)
            {
                if (engine == null || engines.Count >= MaxEngines) continue;
                int index = engines.Count;
                engine.OnEngineDamage += OnDamage;
                disabled[index] = () => { engineOut[index] = true; enginesOut++; };
                engine.OnEngineDisable += disabled[index];
                engineSide[index] = SideOf(aircraft.transform, engine.transform.position, EngineBand);
                engines.Add(engine);
            }
            if (partX.Length == 0)
            {
                List<UnitPart> parts = aircraft.partLookup;
                partX = new float[parts != null ? parts.Count : 0];
                for (int i = 0; i < partX.Length; i++)
                    partX[i] = parts[i] != null ? aircraft.transform.InverseTransformPoint(parts[i].transform.position).x : 0f;
                Rotary = aircraft.GetComponentInChildren<RotorShaft>(true) != null;
            }
        }

        private void OnDamage() => engineDamaged = true;
    }
}
