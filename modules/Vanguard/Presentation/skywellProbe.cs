using System.Collections.Generic;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// Sim-only probes (nomodkit scenarios read them through the status action): what the player would see wrong, as numbers. The
    /// tender against the receiver's bounds while it serves, the joints really having been driven, and how close any two friendly
    /// aircraft ever came. Nothing here runs outside a scenario.
    /// </summary>
    internal sealed partial class SkywellVisuals
    {
        private static readonly bool SimRun = !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO"));

        // Tender root to the receiver's airframe bounds while the host is in Service (m): worst, best, samples.
        internal static float ServeGapMax, ServeGapMin = 999f;
        internal static int ServeGapSamples;
        // The joints really were driven: loader reach and boom plug weight peaks, wing spread, nacelle tilt low, fan turns, tether length.
        internal static float ProbeReachMax, ProbeBoomMax, ProbeSpreadMax, ProbeTiltMin = 90f, ProbeFanTurns, ProbeTetherMax;
        // Animated joints the last rig found in the kit model by name (27 = the whole SkywellRig contract; a stale bundle finds fewer).
        internal static int ProbeJoints;
        // Closest approach between any two aircraft of one side (m).
        internal static float ProbeMinSeparation = 999f;
        internal static string TenderProbe = "none";

        private static float nextSeparation;

        /// <summary>Sim camera: where this tanker's tender is right now (world); false until its rig is built.</summary>
        internal static bool TenderWorld(uint tanker, out Vector3 pos)
        {
            pos = default;
            if (Instance == null || !Instance.rigs.TryGetValue(tanker, out Rig rig) || !rig.Tender.Ok) return false;
            pos = rig.Tender.T.position;
            return true;
        }

        private static void Measure(Rig rig, SkywellView v, Aircraft px, bool poseOn, bool working, Vector3 pos)
        {
            ProbeReachMax = Mathf.Max(ProbeReachMax, rig.Reach);
            ProbeBoomMax = Mathf.Max(ProbeBoomMax, rig.Swing);
            Transform tt = rig.Tanker.transform;
            TenderProbe = $"{v.State} pose={(rig.PoseStage == StageFuel ? "fuel" : rig.PoseStage == StageStation ? "ammo" + rig.PoseStation : rig.PoseStage == StageBelly ? "belly" : "tow")} " +
                          $"job={v.Job}/{v.JobIndex}of{v.JobCount} tender={tt.InverseTransformPoint(pos).ToString("F1")} gap={rig.Gap:F1} arm={rig.ArmClock:F1}s " +
                          $"boom={rig.Swing:F2} reach={rig.Reach:F2} unfold={rig.Unfold:F2} rounds={v.Rounds} tilt={rig.Tilt:F0}";
            if (v.State != TenderState.Service || !poseOn || px == null) return;
            Bounds b = SkywellBoard.LocalBounds(px);
            float gap = SkywellTender.BoxDistance(px.transform.InverseTransformPoint(pos), b.center, b.extents);
            ServeGapMax = Mathf.Max(ServeGapMax, gap);
            ServeGapMin = Mathf.Min(ServeGapMin, gap);
            ServeGapSamples++;
        }

        private static void SampleSeparation()
        {
            if (Time.unscaledTime < nextSeparation) return;
            nextSeparation = Time.unscaledTime + 0.25f;
            IReadOnlyList<Aircraft> all = UnitRegistry.allAircraft;
            for (int i = 0; i < all.Count; i++)
            {
                Aircraft a = all[i];
                if (a == null || a.disabled) continue;
                for (int j = i + 1; j < all.Count; j++)
                {
                    Aircraft c = all[j];
                    if (c == null || c.disabled || c.NetworkHQ != a.NetworkHQ) continue;
                    ProbeMinSeparation = Mathf.Min(ProbeMinSeparation, Vector3.Distance(a.transform.position, c.transform.position));
                }
            }
        }
    }
}
