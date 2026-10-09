using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// MALD-J cockpit lines for the local pilot: "IN SHADOW", "BURN-THROUGH 8.7 km", "SHADOW LOST" for any friendly
    /// pilot, "SHADOW ACTIVE / n RADARS BLIND" for the launcher. Same geometry as the radar filter, evaluated from
    /// synced positions, so it works on clients without a message.
    /// </summary>
    internal sealed class MaldjHud : MonoBehaviour, ISceneService
    {
        private const string Owner = "vanguard.maldj";
        private const string Channel = "vg.maldj";
        private const float EvalInterval = 0.25f;
        private const float RadarListInterval = 1f;

        private readonly List<Radar> radars = new List<Radar>();
        private IHudBoard board;
        private IHudLine pilotLine;
        private IHudLine launcherLine;
        private float nextEval;
        private float nextList;
        private ShadowState state;
        private float burnRange;
        private bool launcher;
        private int lobeRadars;

        public void ResetForScene()
        {
            MaldjField.Jammers.Clear();
            radars.Clear();
            state = ShadowState.None;
            launcher = false;
            pilotLine?.Release();
            launcherLine?.Release();
            pilotLine = launcherLine = null;
        }

        private void Update()
        {
            if (MaldjField.Jammers.Count == 0 && state == ShadowState.None && !launcher) return;
            float now = Time.time;
            if (now >= nextEval)
            {
                nextEval = now + EvalInterval;
                Evaluate(now);
            }
            Draw();
        }

        private void Evaluate(float now)
        {
            if (!GameManager.GetLocalAircraft(out Aircraft me) || me == null || me.disabled)
            {
                Enter(ShadowState.None);
                launcher = false;
                return;
            }
            if (now >= nextList)
            {
                nextList = now + RadarListInterval;
                ListEnemyRadars(me.NetworkHQ);
            }
            launcher = MaldjField.HasJamming(me);
            int lobe = 0, shadowed = 0;
            float nearest = float.MaxValue;
            for (int i = 0; i < radars.Count; i++)
            {
                Radar radar = radars[i];
                if (radar == null || !radar.IsOperational()) continue;
                GlobalPosition at = MaldjField.ScanPosition(radar);
                ShadowState s = MaldjField.Check(at, me, out float range);
                if (s == ShadowState.Shadowed) shadowed++;
                else if (s == ShadowState.BurnThrough) nearest = Mathf.Min(nearest, range);
                if (launcher && MaldjField.InLobeOf(me, at)) lobe++;
            }
            burnRange = nearest;
            lobeRadars = lobe;
            if (launcher) VanguardStats.Counters["maldj.lobeRadars"] = lobe; // gauge for the sim
            // Any radar burning through outranks the radars still blinded: that one can see you.
            Enter(nearest < float.MaxValue ? ShadowState.BurnThrough : shadowed > 0 ? ShadowState.Shadowed : ShadowState.None);
        }

        private void Enter(ShadowState next)
        {
            if (next == state) return;
            if (next == ShadowState.Shadowed) VanguardStats.Bump("maldj.hudShadow");
            else if (next == ShadowState.BurnThrough) VanguardStats.Bump("maldj.hudBurnThrough");
            else
            {
                VanguardStats.Bump("maldj.hudLost");
                if (board != null || ModuleServices.TryGet(out board)) board.Notice(Channel, HudTone.Caution, "SHADOW LOST");
            }
            state = next;
        }

        // Every enemy unit carrying a radar (SAM sites, ships, fighters), de-duplicated: launchers share their site radar.
        private void ListEnemyRadars(FactionHQ mine)
        {
            radars.Clear();
            List<Unit> units = UnitRegistry.allUnits;
            for (int i = 0; i < units.Count; i++)
            {
                Unit unit = units[i];
                if (unit == null || unit.disabled || unit.NetworkHQ == null || unit.NetworkHQ == mine) continue;
                if (unit.radar is Radar radar && radar != null && !radars.Contains(radar)) radars.Add(radar);
            }
        }

        private void Draw()
        {
            if (board == null && !ModuleServices.TryGet(out board)) return;
            board.DeclareChannel(Channel, "SHADOW");
            if (state == ShadowState.Shadowed)
                (pilotLine ??= board.Acquire(Owner, Channel, "pilot"))?.Set(HudTone.Info, "IN SHADOW", null, 0f);
            else if (state == ShadowState.BurnThrough)
                (pilotLine ??= board.Acquire(Owner, Channel, "pilot"))?.Set(HudTone.Warning, "BURN-THROUGH",
                    (burnRange / 1000f).ToString("F1") + " km", Mathf.Clamp01(burnRange / MaldjShadow.BurnThroughRange));
            else
            {
                pilotLine?.Release();
                pilotLine = null;
            }
            if (launcher)
                (launcherLine ??= board.Acquire(Owner, Channel, "launcher"))?.Set(HudTone.Info, "SHADOW ACTIVE",
                    lobeRadars + (lobeRadars == 1 ? " RADAR BLIND" : " RADARS BLIND"), 0f);
            else
            {
                launcherLine?.Release();
                launcherLine = null;
            }
        }
    }
}
