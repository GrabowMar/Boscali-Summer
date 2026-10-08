using NuclearOption.Networking;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Support.Visuals
{
    /// <summary>
    /// Dev hook for the unattended effect check (<c>tests/ingame/ops-effects.json</c>): plays one OPS effect's visual at a map point without touching the allocation, the
    /// fronts or the host effect books. <c>fx</c> is sweep, optical, pulse, jam, sam, team, sabotage, lase, rod, emp, flare, launch or fronts-launch; <c>x</c>/<c>z</c> are
    /// global map metres. Nothing runs unless called.
    /// </summary>
    public static class OpsFxDemo
    {
        public static Dictionary<string, object> Fire(Dictionary<string, object> args)
        {
            string fx = Text(args, "fx") ?? "";
            float radius = Number(args, "radius", 2500f);
            var at = new GlobalPosition(Number(args, "x", 0f), 0f, Number(args, "z", 3000f));
            Vector3 ground = AreaFx.At(at);
            FactionHQ own = GameManager.GetLocalPlayer<Player>(out Player p) && p != null ? p.HQ : null;
            switch (fx)
            {
                case "probe": return new Dictionary<string, object> { { "ok", true }, { "x", at.x }, { "z", at.z }, { "y", ground.y }, { "sea", Datum.LocalSeaY } };
                case "sweep": AreaFx.Sweep(ground, radius, 8f, AreaFx.ReconTint, own); break;
                case "optical": AreaFx.Sweep(ground, radius, 8f, AreaFx.OpticalTint, own); break;
                case "team": AreaFx.Sweep(ground, radius, 8f, AreaFx.TeamTint, own); break;
                case "pulse": AreaFx.Pulse(ground, radius, own); break;
                case "jam": AreaFx.Jam(ground, radius, Number(args, "seconds", 30f), AreaFx.BlindTint, own); break;
                case "sam": AreaFx.Jam(ground, radius, Number(args, "seconds", 30f), AreaFx.SamTint, own); break;
                case "sabotage": StagedBlastFx.Play(ground, 14f); break;
                case "lase":
                    Unit target = null; float best = float.MaxValue;
                    if (UnitRegistry.allUnits != null)
                        foreach (Unit u in UnitRegistry.allUnits)
                        {
                            if (u == null || u.disabled || u is Aircraft) continue;
                            float d = (u.transform.position - ground).sqrMagnitude;
                            if (d < best) { best = d; target = u; }
                        }
                    if (target != null) LaseFx.Play(target, null, 12f);
                    break;
                case "rod": KineticRodStrikeVisuals.TriggerImpact(ground); break;
                case "descent": new GameObject("RodDescentDemo").AddComponent<RodDescentDemo>().Begin(ground); break;
                case "emp": EmpVisualEffect.Trigger(ground + Vector3.up * 6000f, radius); break;
                case "flare": FlareMissileBurstVisuals.TriggerBarrage(ground + Vector3.up * 1200f, radius, 15f, 36, own); break;
                case "launch": SatelliteLaunchVisuals.Play(ground, 16f); break;
                case "rear":
                    if (FrontEffectVisuals.TryRearAirbase(own, out Vector3 site)) SatelliteLaunchVisuals.Play(site, 16f);
                    break;
                default: return new Dictionary<string, object> { { "ok", false }, { "error", "unknown fx '" + fx + "'" } };
            }
            return new Dictionary<string, object> { { "ok", true }, { "fx", fx }, { "groundY", ground.y } };
        }
    }

    /// <summary>Dev only: flies a stand-in rod down a 6 km line at 1.5 km/s through the real trail, then the real strike, so the descent look can be checked without a missile.</summary>
    internal sealed class RodDescentDemo : MonoBehaviour
    {
        private RodTrailFx trail;
        private Vector3 from, to, last;
        private float t;

        public void Begin(Vector3 ground)
        {
            to = ground; from = ground + new Vector3(-1800f, 6200f, -2600f);
            last = from;
            trail = KineticRodStrikeVisuals.RentTrail();
            trail.Begin();
        }

        private void LateUpdate()
        {
            t += Time.deltaTime / 3f;
            Vector3 p = Vector3.Lerp(from, to, Mathf.Clamp01(t));
            trail.Feed(last, p, Time.deltaTime);
            last = p;
            if (t >= 1f)
            {
                trail.End();
                KineticRodStrikeVisuals.TriggerImpact(to);
                Destroy(gameObject);
            }
        }
    }
}
